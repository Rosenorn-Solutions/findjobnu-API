using FindjobnuService.DTOs;
using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace FindjobnuService.Services;

public class CvService : ICvService
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxExtractedCharacters = 2_500_000;
    private const int MaxResultCharacters = 2_000_000;
    private const int MaxArraySegmentCharacters = 200_000;

    private readonly FindjobnuContext _db;
    private readonly IProfileService _profileService;
    private readonly ILogger<CvService> _logger;

    public CvService(FindjobnuContext db, IProfileService profileService, ILogger<CvService> logger)
    {
        _db = db;
        _profileService = profileService;
        _logger = logger;
    }

    public async Task<CvReadabilityResult> AnalyzeAsync(IFormFile pdfFile, CancellationToken cancellationToken = default)
    {
        ValidateFile(pdfFile);
        var text = await ExtractTextAsync(pdfFile, cancellationToken);
        var summary = BuildSummary(text);
        var score = ComputeReadabilityScore(text);
        return new CvReadabilityResult(text, score, summary);
    }

    public async Task<CvImportResult> ImportToProfileAsync(string userId, IFormFile pdfFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        ValidateFile(pdfFile);
        var text = await ExtractTextAsync(pdfFile, cancellationToken);
        var summary = BuildSummary(text);
        var extracted = ExtractProfileData(text);

        var profile = await _db.Profiles
            .Include(p => p.BasicInfo)
            .Include(p => p.Experiences)
            .Include(p => p.Educations)
            .Include(p => p.Skills)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var created = profile == null;
        if (profile == null)
        {
            profile = new Profile
            {
                UserId = userId,
                BasicInfo = new BasicInfo()
            };
            _db.Profiles.Add(profile);
        }
        else
        {
            profile.LastUpdatedAt = DateTime.UtcNow;
            profile.BasicInfo ??= new BasicInfo();
        }

        ApplyExtraction(profile, extracted, created);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = await _profileService.GetByUserIdAsync(userId);
        if (dto == null)
        {
            throw new InvalidOperationException("Profile could not be loaded after import.");
        }

        return new CvImportResult(dto, summary, text, created, extracted.Warnings);
    }

    private static void ValidateFile(IFormFile pdfFile)
    {
        if (pdfFile == null || pdfFile.Length == 0)
        {
            throw new ArgumentException("PDF file is required.");
        }

        if (!pdfFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Unsupported file extension. Only .pdf is allowed.");
        }

        if (pdfFile.Length > MaxFileSizeBytes)
        {
            throw new ArgumentException($"File too large. Max allowed size is {MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        if (!string.Equals(pdfFile.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(pdfFile.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid Content-Type. Expecting application/pdf.");
        }
    }

    private async Task<string> ExtractTextAsync(IFormFile pdfFile, CancellationToken cancellationToken)
    {
        string text;
        using (var ms = new MemoryStream((int)pdfFile.Length))
        {
            await pdfFile.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            if (!LooksLikePdf(ms))
            {
                throw new ArgumentException("The uploaded file does not appear to be a valid PDF.");
            }

            ms.Position = 0;
            text = ExtractTextWithIText(ms);
            if (string.IsNullOrWhiteSpace(text))
            {
                ms.Position = 0;
                text = ExtractTextFromPdf(ms);
            }
        }

        return text;
    }

    private void ApplyExtraction(Profile profile, CvExtractionResult data, bool created)
    {
        if (!string.IsNullOrWhiteSpace(data.FirstName))
            profile.BasicInfo.FirstName = data.FirstName;
        if (!string.IsNullOrWhiteSpace(data.LastName))
            profile.BasicInfo.LastName = data.LastName;
        if (!string.IsNullOrWhiteSpace(data.PhoneNumber))
            profile.BasicInfo.PhoneNumber = data.PhoneNumber;
        if (!string.IsNullOrWhiteSpace(data.About))
            profile.BasicInfo.About = data.About;
        if (!string.IsNullOrWhiteSpace(data.Location))
            profile.BasicInfo.Location = data.Location;
        if (!string.IsNullOrWhiteSpace(data.Company))
            profile.BasicInfo.Company = data.Company;
        if (!string.IsNullOrWhiteSpace(data.JobTitle))
            profile.BasicInfo.JobTitle = data.JobTitle;

        if (data.Keywords.Count > 0)
        {
            var merged = profile.Keywords ?? new List<string>();
            foreach (var kw in data.Keywords)
            {
                if (!merged.Contains(kw, StringComparer.OrdinalIgnoreCase))
                {
                    merged.Add(kw);
                }
            }
            profile.Keywords = merged;
        }

        if (data.Experiences.Count > 0)
        {
            if (!created)
            {
                _db.Experiences.RemoveRange(profile.Experiences ?? []);
            }
            profile.Experiences = new List<Experience>();
            foreach (var exp in data.Experiences)
            {
                exp.Profile = profile;
                profile.Experiences.Add(exp);
            }
        }

        if (data.Educations.Count > 0)
        {
            if (!created)
            {
                _db.Educations.RemoveRange(profile.Educations ?? []);
            }
            profile.Educations = new List<Education>();
            foreach (var edu in data.Educations)
            {
                edu.Profile = profile;
                profile.Educations.Add(edu);
            }
        }

        if (data.Skills.Count > 0)
        {
            if (!created)
            {
                _db.Skills.RemoveRange(profile.Skills ?? []);
            }
            profile.Skills = new List<Skill>();
            foreach (var skill in data.Skills)
            {
                skill.Profile = profile;
                profile.Skills.Add(skill);
            }
        }
    }

    private static CvExtractionResult ExtractProfileData(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CvExtractionResult(new List<string> { "Ingen tekst kunne udtrækkes fra PDF." });
        }

        var warnings = new List<string>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var (firstName, lastName) = ParseName(lines);
        var phone = ParsePhone(text);
        var about = ExtractSectionText(lines, new[] { "summary", "profile", "about", "om", "bio" });
        var location = ParseLocation(lines);
        var skillsSection = ExtractSectionText(lines, new[] { "skills", "kompetencer" });
        var skills = ParseSkills(skillsSection);
        var experiencesSection = ExtractSectionText(lines, new[] { "experience", "erfaring", "work experience" });
        var experiences = ParseExperiences(experiencesSection);
        var educationsSection = ExtractSectionText(lines, new[] { "education", "uddannelse" });
        var educations = ParseEducations(educationsSection);

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            warnings.Add("Navn blev ikke fundet i CV'et.");
        }
        if (skills.Count == 0)
        {
            warnings.Add("Færdigheder blev ikke identificeret.");
        }
        if (experiences.Count == 0)
        {
            warnings.Add("Erfaring blev ikke identificeret.");
        }

        return new CvExtractionResult
        {
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phone,
            About = about,
            Location = location,
            Experiences = experiences,
            Educations = educations,
            Skills = skills,
            Keywords = skills.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Warnings = warnings
        };
    }

    private static (string First, string Last) ParseName(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^[\p{L}']['\p{L}\s.-]+$"))
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return (parts[0], string.Join(' ', parts.Skip(1)));
                }
            }
        }
        return (string.Empty, string.Empty);
    }

    private static string ParsePhone(string text)
    {
        var match = Regex.Match(text, @"(\n|\s)(\+?\d[\d\s().-]{6,}\d)");
        return match.Success ? match.Groups[2].Value.Trim() : string.Empty;
    }

    private static string ParseLocation(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("Location", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("By", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Sted", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                {
                    return parts[1].Trim();
                }
            }
        }
        return string.Empty;
    }

    private static string ExtractSectionText(string[] lines, string[] sectionHeaders)
    {
        var content = new List<string>();
        bool inSection = false;
        foreach (var line in lines)
        {
            var lower = line.Trim().ToLowerInvariant();
            if (sectionHeaders.Any(h => lower.StartsWith(h)))
            {
                inSection = true;
                var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    content.Add(parts[1]);
                }
                continue;
            }

            var isStopHeader = DefaultSectionKeywords.Any(h => lower.StartsWith(h)) && !sectionHeaders.Any(h => lower.StartsWith(h));
            if (inSection && isStopHeader)
            {
                break;
            }

            if (inSection)
            {
                content.Add(line);
            }
        }
        return string.Join('\n', content);
    }

    private static List<Skill> ParseSkills(string sectionText)
    {
        var skills = new List<Skill>();
        if (string.IsNullOrWhiteSpace(sectionText)) return skills;

        var tokens = sectionText
            .Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length <= 80);

        foreach (var token in tokens)
        {
            skills.Add(new Skill
            {
                Name = token,
                Proficiency = SkillProficiency.Intermediate
            });
        }

        return skills;
    }

    private static List<Experience> ParseExperiences(string sectionText)
    {
        var experiences = new List<Experience>();
        if (string.IsNullOrWhiteSpace(sectionText)) return experiences;

        var chunks = SplitByBlankLines(sectionText);
        foreach (var chunk in chunks)
        {
            var lines = chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) continue;
            var first = lines[0];
            var (company, role) = ParseCompanyAndRole(first);
            var description = string.Join('\n', lines.Skip(1));
            experiences.Add(new Experience
            {
                Company = company,
                PositionTitle = role,
                Description = description
            });
        }

        return experiences;
    }

    private static List<Education> ParseEducations(string sectionText)
    {
        var educations = new List<Education>();
        if (string.IsNullOrWhiteSpace(sectionText)) return educations;

        var chunks = SplitByBlankLines(sectionText);
        foreach (var chunk in chunks)
        {
            var lines = chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) continue;
            var first = lines[0];
            var parts = first.Split('-', StringSplitOptions.TrimEntries);
            var institution = parts.FirstOrDefault() ?? string.Empty;
            var degree = parts.Skip(1).FirstOrDefault() ?? string.Empty;
            var description = string.Join('\n', lines.Skip(1));
            educations.Add(new Education
            {
                Institution = institution,
                Degree = degree,
                Description = description
            });
        }

        return educations;
    }

    private static List<string> SplitByBlankLines(string sectionText)
    {
        var results = new List<string>();
        var sb = new StringBuilder();
        using var reader = new StringReader(sectionText);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (sb.Length > 0)
                {
                    results.Add(sb.ToString().Trim());
                    sb.Clear();
                }
            }
            else
            {
                sb.AppendLine(line.Trim());
            }
        }
        if (sb.Length > 0)
        {
            results.Add(sb.ToString().Trim());
        }
        return results;
    }

    private static (string Company, string Role) ParseCompanyAndRole(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return (string.Empty, string.Empty);
        if (line.Contains('-'))
        {
            var parts = line.Split('-', 2, StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
        }
        return (line, string.Empty);
    }

    private static bool LooksLikePdf(Stream stream)
    {
        long originalPos = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (!stream.CanSeek) return false;

            stream.Position = 0;
            Span<byte> header = stackalloc byte[5];
            if (!TryFillBuffer(stream, header))
                return false;
            if (header[0] != (byte)'%' || header[1] != (byte)'P' || header[2] != (byte)'D' || header[3] != (byte)'F' || header[4] != (byte)'-')
                return false;

            var tailSize = (int)Math.Min(1024, stream.Length);
            stream.Position = stream.Length - tailSize;
            var tailBuf = new byte[tailSize];
            if (!TryFillBuffer(stream, tailBuf))
                return false;
            var tailStr = Encoding.ASCII.GetString(tailBuf);
            if (!tailStr.Contains("%%EOF", StringComparison.Ordinal))
                return false;

            stream.Position = 0;
            var headSize = (int)Math.Min(4096, stream.Length);
            var headBuf = new byte[headSize];
            if (!TryFillBuffer(stream, headBuf))
                return false;
            var headStr = Encoding.ASCII.GetString(headBuf);
            if (headStr.Contains("/Encrypt", StringComparison.Ordinal))
                throw new ArgumentException("Encrypted/password-protected PDFs are not supported.");

            return true;
        }
        finally
        {
            if (stream.CanSeek) stream.Position = originalPos;
        }
    }

    private static string ExtractTextWithIText(Stream pdfStream)
    {
        try
        {
            var sb = new StringBuilder();
            using var reader = new PdfReader(pdfStream);
            using var pdf = new PdfDocument(reader);
            int pages = pdf.GetNumberOfPages();
            for (int i = 1; i <= pages; i++)
            {
                var strategy = new LocationTextExtractionStrategy();
                var text = PdfTextExtractor.GetTextFromPage(pdf.GetPage(i), strategy);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
                if (sb.Length > MaxExtractedCharacters) break;
            }

            var result = sb.ToString();
            result = Regex.Replace(result, "-\r?\n", string.Empty);
            result = Regex.Replace(result, "\r?\n", "\n");
            result = Regex.Replace(result, "[\t\u00A0]", " ");
            result = Regex.Replace(result, "[ ]{2,}", " ");
            result = Regex.Replace(result, "\n{3,}", "\n\n");
            return result.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractTextFromPdf(Stream pdfStream)
    {
        try
        {
            var memoryStream = EnsureMemoryStream(pdfStream, out var ownsStream);
            try
            {
                var combinedContent = ExtractDecodedStreams(memoryStream);
                if (string.IsNullOrEmpty(combinedContent))
                {
                    combinedContent = ReadRawPdfContent(memoryStream);
                }

                var text = ExtractTextFromContentStreams(combinedContent);
                if (string.IsNullOrWhiteSpace(text))
                {
                    memoryStream.Position = 0;
                    text = NormalizeWhitespace(ReadRawPdfContent(memoryStream));
                }

                return text;
            }
            finally
            {
                if (ownsStream)
                {
                    memoryStream.Dispose();
                }
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static MemoryStream EnsureMemoryStream(Stream source, out bool ownsStream)
    {
        if (source is MemoryStream memoryStream)
        {
            ownsStream = false;
            memoryStream.Position = 0;
            return memoryStream;
        }

        var copy = new MemoryStream();
        source.CopyTo(copy);
        copy.Position = 0;
        ownsStream = true;
        return copy;
    }

    private static string ExtractDecodedStreams(MemoryStream pdfStream)
    {
        var data = pdfStream.ToArray();
        var content = new StringBuilder();

        foreach (var streamContent in EnumeratePdfStreams(data))
        {
            var decoded = TryDecodeStream(streamContent.Raw, streamContent.IsFlate) ?? string.Empty;
            if (decoded.Length == 0) continue;

            content.AppendLine(decoded);
            if (content.Length > MaxExtractedCharacters) break;
        }

        return content.ToString();
    }

    private static string ReadRawPdfContent(Stream pdfStream)
    {
        pdfStream.Position = 0;
        using var fallbackReader = new StreamReader(pdfStream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return fallbackReader.ReadToEnd();
    }

    private static string ExtractTextFromContentStreams(string combined)
    {
        if (string.IsNullOrWhiteSpace(combined))
        {
            return string.Empty;
        }

        var results = new List<string>();
        int currentLength = 0;

        ProcessTjOperators(combined, results, ref currentLength);

        if (!HasExceededResultLimit(currentLength))
        {
            ProcessTjArrayOperators(combined, results, ref currentLength);
        }

        if (!HasExceededResultLimit(currentLength))
        {
            ProcessQuoteOperators(combined, results, ref currentLength);
        }

        if (results.Count == 0) return string.Empty;

        return NormalizeWhitespace(string.Join("\n", results));
    }

    private static void ProcessTjOperators(string combined, List<string> results, ref int currentLength)
    {
        var matches = Regex.Matches(combined, "\\((?<s>(?:\\\\.|[^\\\\\\)])*)\\)\\s*T[jJ]");
        foreach (Match match in matches)
        {
            var text = UnescapePdfString(match.Groups["s"].Value);
            if (!TryAddResult(results, text, ref currentLength))
            {
                break;
            }
        }
    }

    private static void ProcessTjArrayOperators(string combined, List<string> results, ref int currentLength)
    {
        var arrayMatches = Regex.Matches(combined, "\\[(?<arr>[^\\]]*)\\]\\s*TJ");
        foreach (Match match in arrayMatches)
        {
            var arr = match.Groups["arr"].Value;
            var sb = new StringBuilder();
            foreach (Match part in Regex.Matches(arr, "\\((?<s>(?:\\\\.|[^\\\\\\)])*)\\)"))
            {
                sb.Append(UnescapePdfString(part.Groups["s"].Value));
                if (sb.Length > MaxArraySegmentCharacters) break;
            }

            var text = sb.ToString();
            if (!TryAddResult(results, text, ref currentLength))
            {
                break;
            }
        }
    }

    private static void ProcessQuoteOperators(string combined, List<string> results, ref int currentLength)
    {
        ProcessQuotePattern(combined, results, ref currentLength, "\\((?<s>(?:\\\\.|[^\\\\\\)])*)\\)\\s*'");
        if (HasExceededResultLimit(currentLength))
        {
            return;
        }

        ProcessQuotePattern(combined, results, ref currentLength, "\\((?<s>(?:\\\\.|[^\\\\\\)])*)\\)\\s*\"");
    }

    private static void ProcessQuotePattern(string combined, List<string> results, ref int currentLength, string pattern)
    {
        var matches = Regex.Matches(combined, pattern);
        foreach (Match match in matches)
        {
            var text = UnescapePdfString(match.Groups["s"].Value);
            if (!TryAddResult(results, text, ref currentLength))
            {
                break;
            }
        }
    }

    private static bool TryAddResult(List<string> results, string text, ref int currentLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return !HasExceededResultLimit(currentLength);
        }

        results.Add(text);
        currentLength += text.Length;
        return !HasExceededResultLimit(currentLength);
    }

    private static bool HasExceededResultLimit(int currentLength) => currentLength > MaxResultCharacters;

    private readonly struct PdfStreamChunk
    {
        public PdfStreamChunk(byte[] raw, bool isFlate)
        {
            Raw = raw;
            IsFlate = isFlate;
        }
        public byte[] Raw { get; }
        public bool IsFlate { get; }
    }

    private static IEnumerable<PdfStreamChunk> EnumeratePdfStreams(byte[] data)
    {
        var ascii = Encoding.ASCII;
        var text = ascii.GetString(data);
        int index = 0;
        while (index < text.Length)
        {
            int streamPos = text.IndexOf("stream", index, StringComparison.Ordinal);
            if (streamPos == -1) yield break;

            int dictEnd = streamPos;
            int dictStart = text.LastIndexOf("<<", dictEnd, dictEnd);
            string dict = dictStart >= 0 ? text.Substring(dictStart, dictEnd - dictStart) : string.Empty;
            bool isFlate = dict.Contains("/FlateDecode", StringComparison.Ordinal) || dict.Contains("/Fl", StringComparison.Ordinal);

            int dataStart = streamPos + "stream".Length;
            if (dataStart < text.Length && (text[dataStart] == '\r' || text[dataStart] == '\n'))
            {
                if (text[dataStart] == '\r' && dataStart + 1 < text.Length && text[dataStart + 1] == '\n') dataStart += 2; else dataStart += 1;
            }

            int endStreamPos = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (endStreamPos == -1) yield break;

            int byteStart = ascii.GetByteCount(text.Substring(0, dataStart));
            int byteEnd = ascii.GetByteCount(text.Substring(0, endStreamPos));
            if (byteEnd > data.Length || byteStart > data.Length || byteEnd <= byteStart)
            {
                index = endStreamPos + 9;
                continue;
            }

            var length = byteEnd - byteStart;
            var raw = new byte[length];
            Buffer.BlockCopy(data, byteStart, raw, 0, length);
            yield return new PdfStreamChunk(raw, isFlate);

            index = endStreamPos + 9;
        }
    }

    private static string? TryDecodeStream(byte[] raw, bool isFlate)
    {
        if (isFlate)
        {
            try
            {
                using var input = new MemoryStream(raw);
                using var z = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: false);
                using var reader = new StreamReader(z, Encoding.Latin1);
                return reader.ReadToEnd();
            }
            catch
            {
                try
                {
                    using var input = new MemoryStream(raw);
                    using var def = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false);
                    using var reader = new StreamReader(def, Encoding.Latin1);
                    return reader.ReadToEnd();
                }
                catch
                {
                }
            }
        }

        try
        {
            return Encoding.Latin1.GetString(raw);
        }
        catch
        {
            return null;
        }
    }

    private static string UnescapePdfString(string s)
    {
        var sb = new StringBuilder();
        int index = 0;
        while (index < s.Length)
        {
            if (s[index] != '\\' || index + 1 >= s.Length)
            {
                sb.Append(s[index]);
                index++;
                continue;
            }

            if (TryAppendEscapedCharacter(s, index, sb, out var consumed))
            {
                index += consumed;
            }
            else
            {
                sb.Append(s[index + 1]);
                index += 2;
            }
        }

        return sb.ToString();
    }

    private static bool TryAppendEscapedCharacter(string source, int escapeStartIndex, StringBuilder target, out int consumed)
    {
        var nextChar = source[escapeStartIndex + 1];
        switch (nextChar)
        {
            case '\\':
            case '(':
            case ')':
                target.Append(nextChar);
                consumed = 2;
                return true;
            case 'n':
                target.Append('\n');
                consumed = 2;
                return true;
            case 'r':
                target.Append('\r');
                consumed = 2;
                return true;
            case 't':
                target.Append('\t');
                consumed = 2;
                return true;
            default:
                if (char.IsDigit(nextChar))
                {
                    int digits = 1;
                    while (escapeStartIndex + 1 + digits < source.Length && digits < 3 && char.IsDigit(source[escapeStartIndex + 1 + digits]))
                    {
                        digits++;
                    }

                    var octalSegment = source.Substring(escapeStartIndex + 1, digits);
                    if (TryParseOctalValue(octalSegment, out var value))
                    {
                        target.Append(value);
                        consumed = 1 + digits;
                        return true;
                    }
                }

                consumed = 0;
                return false;
        }
    }

    private static bool TryParseOctalValue(string octalSegment, out char value)
    {
        value = default;
        if (string.IsNullOrEmpty(octalSegment))
        {
            return false;
        }

        int total = 0;
        foreach (var ch in octalSegment)
        {
            if (ch < '0' || ch > '7')
            {
                return false;
            }

            total = (total << 3) + (ch - '0');
        }

        value = (char)total;
        return true;
    }

    private static bool TryFillBuffer(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer.Slice(totalRead));
            if (read == 0)
            {
                return false;
            }
            totalRead += read;
        }

        return true;
    }

    private static string NormalizeWhitespace(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        input = Regex.Replace(input, "-\r?\n", string.Empty);
        var normalized = Regex.Replace(input, "\r?\n", "\n");
        normalized = Regex.Replace(normalized, "[\t\u00A0]", " ");
        normalized = Regex.Replace(normalized, "[ ]{2,}", " ");
        normalized = Regex.Replace(normalized, "\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static readonly string[] DefaultSectionKeywords = new[]
    {
        "experience", "education", "skills", "projects", "summary", "profile",
        "erfaring", "uddannelse", "færdigheder", "projekter", "om", "om mig", "bio", "profil", "kontakt"
    };

    private static CvReadabilitySummary BuildSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CvReadabilitySummary(
                TotalChars: 0,
                TotalWords: 0,
                TotalLines: 0,
                HasEmail: false,
                HasPhone: false,
                BulletCount: 0,
                MatchedSections: 0,
                TotalSectionKeywords: DefaultSectionKeywords.Length,
                Note: "Ingen tekst kunne udtrækkes. PDF'en kan være billedbaseret eller beskyttet."
            );
        }

        var totalChars = text.Length;
        var totalWords = Regex.Matches(text, @"\b[\r\n\p{L}\p{Nd}\-_.]+\b").Count;
        var totalLines = text.Split('\n').Length;

        var hasEmail = Regex.IsMatch(text, @"[A-Z0-9._%+-]+\s*@\s*[A-Z0-9.-]+\s*\.\s*[A-Z]{2,}", RegexOptions.IgnoreCase);
        var hasPhone = Regex.IsMatch(text, @"(\n|\s)(\+?\d[\d\s().-]{6,}\d)");
        var bulletCount = Regex.Matches(text, @"(^|\n)[\u2022\-*] \s?").Count;
        var matchedSections = DefaultSectionKeywords.Count(k => Regex.IsMatch(text, $@"(^|\n)\s*{Regex.Escape(k)}\b", RegexOptions.IgnoreCase));

        return new CvReadabilitySummary(
            TotalChars: totalChars,
            TotalWords: totalWords,
            TotalLines: totalLines,
            HasEmail: hasEmail,
            HasPhone: hasPhone,
            BulletCount: bulletCount,
            MatchedSections: matchedSections,
            TotalSectionKeywords: DefaultSectionKeywords.Length,
            Note: null
        );
    }

    private static double ComputeReadabilityScore(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0.0;

        double score = 50.0;

        var words = Regex.Matches(text, @"\b[\r\n\p{L}\p{Nd}\-_.]+\b").Count;
        if (words < 100) score -= 10;
        else if (words > 1500) score -= 10;
        else score += 5;

        if (Regex.IsMatch(text, @"[A-Z0-9._%+-]+\s*@\s*[A-Z0-9.-]+\s*\.\s*[A-Z]{2,}", RegexOptions.IgnoreCase)) score += 10;
        if (Regex.IsMatch(text, @"(\n|\s)(\+?\d[\d\s().-]{6,}\d)")) score += 5;

        var bulletCount = Regex.Matches(text, @"(^|\n)[\u2022\-*] \s?").Count;
        score += Math.Min(10, bulletCount);

        var sectionKeywords = new[] { "experience", "education", "skills", "projects", "summary", "profile", "erfaring", "uddannelse", "færdigheder", "projekter", "om", "om mig", "bio", "profil", "resumé", "resume" };
        var sections = sectionKeywords.Count(k => Regex.IsMatch(text, $@"(^|\n)\s*{Regex.Escape(k)}\b", RegexOptions.IgnoreCase));
        score += sections * 5;

        var nonLetterRatio = text.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != '-' && c != '.') / (double)text.Length;
        if (nonLetterRatio > 0.2) score -= 10;

        return Math.Max(0, Math.Min(100, score));
    }

    private sealed class CvExtractionResult
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string About { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public List<Experience> Experiences { get; set; } = new();
        public List<Education> Educations { get; set; } = new();
        public List<Skill> Skills { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public CvExtractionResult()
        {
        }

        public CvExtractionResult(List<string> warnings)
        {
            Warnings = warnings;
        }
    }
}
