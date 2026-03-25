using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlopEvaluator.Health.Models;

namespace SlopEvaluator.Health.Collectors;

/// <summary>
/// Scans for hardcoded secrets, security patterns, auth configuration,
/// and common vulnerability patterns in C# code.
/// </summary>
public class SecurityPostureCollector
{
    private readonly ILogger<SecurityPostureCollector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityPostureCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger{T}"/>.</param>
    public SecurityPostureCollector(ILogger<SecurityPostureCollector>? logger = null)
    {
        _logger = logger ?? NullLogger<SecurityPostureCollector>.Instance;
    }

    // Patterns for detecting hardcoded secrets in source code.
    // Each pattern targets a different credential format: passwords, API keys,
    // tokens with high-entropy values, connection strings, PEM private keys, and Bearer tokens.
    private static readonly Regex[] SecretPatterns =
    [
        new(@"(password|passwd|pwd)\s*=\s*""[^""]+""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(api[_-]?key|apikey)\s*=\s*""[^""]+""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(secret|token)\s*=\s*""[a-zA-Z0-9+/=]{20,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(connection[_-]?string)\s*=\s*""[^""]*(?:Password|pwd)=[^""]+""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----", RegexOptions.Compiled),
        new(@"Bearer\s+[a-zA-Z0-9\-._~+/]+=*", RegexOptions.Compiled),
    ];

    // Patterns for detecting potential SQL injection vulnerabilities.
    // Matches string.Format with SQL keywords, interpolated strings with SQL keywords,
    // and raw SQL execution with string concatenation.
    private static readonly Regex[] SqlInjectionPatterns =
    [
        new(@"string\.Format\(.*(SELECT|INSERT|UPDATE|DELETE)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\$""[^""]*\b(SELECT|INSERT|UPDATE|DELETE)\b[^""]*\{", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\.ExecuteSqlRaw\(.*\+", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Scan source files for hardcoded secrets, security patterns, and vulnerability indicators.
    /// </summary>
    public async Task<SecurityPosture> CollectAsync(string projectPath)
    {
        _logger.LogInformation("Starting security posture scan for {ProjectPath}", projectPath);
        var findings = new List<SecurityFinding>();
        var csFiles = GetSourceFiles(projectPath);

        int filesWithSecrets = 0;
        int filesWithSqlInjection = 0;
        int filesWithInputValidation = 0;
        int totalEndpoints = 0;
        bool hasAuth = false;
        bool hasHttps = false;
        bool hasCors = false;
        bool hasDataProtection = false;
        bool hasAntiforgery = false;

        foreach (var file in csFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            var relativePath = Path.GetRelativePath(projectPath, file);

            // Strip comment lines to avoid false positives from documentation
            var codeOnly = string.Join("\n", content.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//")));

            // Secret detection
            foreach (var pattern in SecretPatterns)
            {
                var matches = pattern.Matches(codeOnly);
                foreach (Match match in matches)
                {
                    // Skip test files and appsettings templates
                    if (relativePath.Contains("Test", StringComparison.OrdinalIgnoreCase)
                        || relativePath.Contains("sample", StringComparison.OrdinalIgnoreCase)
                        || relativePath.Contains("example", StringComparison.OrdinalIgnoreCase))
                        continue;

                    findings.Add(new SecurityFinding
                    {
                        RuleId = "SEC001",
                        Category = "Secrets",
                        Severity = "high",
                        FilePath = relativePath,
                        LineNumber = GetLineNumber(content, match.Index),
                        Description = "Potential hardcoded secret detected",
                        Recommendation = "Move to user-secrets, environment variables, or a vault"
                    });
                    filesWithSecrets++;
                }
            }

            // SQL injection patterns
            foreach (var pattern in SqlInjectionPatterns)
            {
                if (pattern.IsMatch(content))
                {
                    findings.Add(new SecurityFinding
                    {
                        RuleId = "SEC002",
                        Category = "Injection",
                        Severity = "critical",
                        FilePath = relativePath,
                        Description = "Potential SQL injection: string concatenation in query",
                        Recommendation = "Use parameterized queries or EF Core LINQ"
                    });
                    filesWithSqlInjection++;
                }
            }

            // Detect security infrastructure by scanning for well-known ASP.NET Core
            // middleware and attribute patterns. Each flag contributes to a sub-score.
            // Authentication: DI registration, middleware, JWT, or attribute-based auth
            if (content.Contains("AddAuthentication") || content.Contains("UseAuthentication")
                || content.Contains("JwtBearer") || content.Contains("[Authorize]"))
                hasAuth = true;
            // HTTPS enforcement: redirect middleware or strict metadata requirement
            if (content.Contains("UseHttpsRedirection") || content.Contains("RequireHttpsMetadata"))
                hasHttps = true;
            // CORS: cross-origin resource sharing policy configuration
            if (content.Contains("AddCors") || content.Contains("UseCors"))
                hasCors = true;
            // Data Protection API: encryption-at-rest for cookies, tokens, etc.
            if (content.Contains("AddDataProtection") || content.Contains("IDataProtector"))
                hasDataProtection = true;
            // Anti-forgery: CSRF protection for form submissions
            if (content.Contains("ValidateAntiForgeryToken") || content.Contains("AddAntiforgery"))
                hasAntiforgery = true;
            // Input validation: model binding attributes or validation frameworks
            if (content.Contains("[FromBody]") || content.Contains("[FromQuery]")
                || content.Contains("FluentValidation") || content.Contains("DataAnnotations"))
                filesWithInputValidation++;
            // Endpoint count: used to compute input validation coverage ratio
            if (content.Contains("[HttpGet]") || content.Contains("[HttpPost]")
                || content.Contains("MapGet") || content.Contains("MapPost"))
                totalEndpoints++;
        }

        if (!File.Exists(Path.Combine(projectPath, ".editorconfig")))
            _logger.LogWarning("No .editorconfig found for code style enforcement in {Path}", projectPath);

        // Check for .gitignore excluding secrets
        bool gitignoreExcludesSecrets = false;
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            var gitignore = await File.ReadAllTextAsync(gitignorePath);
            gitignoreExcludesSecrets = gitignore.Contains("*.env")
                || gitignore.Contains("appsettings.*.json")
                || gitignore.Contains("secrets");
        }

        _logger.LogInformation("Security scan found {SecretCount} potential secrets across {FileCount} files", filesWithSecrets, csFiles.Count);
        // Concatenate all source content for pattern detection
        var allContent = string.Join("\n", csFiles.Select(f => File.ReadAllText(f)));

        double secretHygiene = ComputeSecretHygiene(filesWithSecrets, csFiles.Count, gitignoreExcludesSecrets);
        double authPatterns = ScoreAuthPatterns(hasAuth, totalEndpoints, allContent);
        double inputValidation = totalEndpoints > 0
            ? Math.Min(1.0, (double)filesWithInputValidation / totalEndpoints)
            : 0.5;
        double httpSecurity = ComputeHttpSecurity(hasHttps, hasCors, hasAntiforgery);
        double dataProtection = ScoreDataProtection(hasDataProtection, allContent);
        double cryptoPractice = ScoreCryptographyPractice(allContent);

        _logger.LogInformation("Security posture score components — SecretHygiene: {SecretHygiene:F3}, AuthPatterns: {AuthPatterns:F3}, HttpSecurity: {HttpSecurity:F3}", secretHygiene, authPatterns, httpSecurity);

        return new SecurityPosture
        {
            SecretHygiene = secretHygiene,
            AuthPatterns = authPatterns,
            InputValidation = Math.Min(1.0, inputValidation),
            CryptographyPractice = cryptoPractice,
            DependencySecurity = 1.0, // populated from DependencyHealth separately
            HttpSecurity = httpSecurity,
            DataProtection = dataProtection,
            Findings = findings,
            Owasp = BuildOwaspCoverage(secretHygiene, authPatterns, inputValidation,
                filesWithSqlInjection, httpSecurity, dataProtection)
        };
    }

    /// <summary>
    /// Score auth patterns based on presence of authentication and authorization infrastructure.
    /// </summary>
    internal static double ScoreAuthPatterns(bool hasAuth, int totalEndpoints, string allContent)
    {
        if (!hasAuth)
            return totalEndpoints > 0 ? 0.2 : 0.5;
        bool hasAuthorization = allContent.Contains("[Authorize]")
            || allContent.Contains("RequireAuthorization");
        return hasAuthorization ? 1.0 : 0.8;
    }

    /// <summary>
    /// Score data protection based on presence and active usage of data protection APIs.
    /// </summary>
    internal static double ScoreDataProtection(bool hasDataProtection, string allContent)
    {
        if (!hasDataProtection) return 0.4;
        bool hasActiveUsage = allContent.Contains("IDataProtector")
            || allContent.Contains("Protect(")
            || allContent.Contains("DataProtectionProvider");
        return hasActiveUsage ? 1.0 : 0.8;
    }

    /// <summary>
    /// Score cryptography practice by detecting usage of .NET cryptography APIs.
    /// </summary>
    internal static double ScoreCryptographyPractice(string allContent)
    {
        bool hasCryptoUsage = allContent.Contains("System.Security.Cryptography")
            || allContent.Contains("DataProtection")
            || allContent.Contains("SHA256") || allContent.Contains("HMAC")
            || allContent.Contains("Aes") || allContent.Contains("RSA");
        return hasCryptoUsage ? 0.8 : 0.5;
    }

    /// <summary>
    /// Score secret hygiene based on number of hardcoded secrets found.
    /// Tiered scoring: 0 secrets = perfect, 1-2 = 0.6, 3-5 = 0.3, 6+ = 0.1.
    /// A .gitignore that excludes secret files earns a +0.1 bonus (capped at 1.0).
    /// </summary>
    internal static double ComputeSecretHygiene(int secretFindings, int totalFiles, bool gitignoreExcludes)
    {
        if (totalFiles == 0) return 1.0;
        double base_score = secretFindings == 0 ? 1.0
            : secretFindings <= 2 ? 0.6
            : secretFindings <= 5 ? 0.3
            : 0.1;
        return gitignoreExcludes ? Math.Min(1.0, base_score + 0.1) : base_score;
    }

    /// <summary>
    /// Score HTTP security as a checklist: HTTPS, CORS, and anti-forgery.
    /// Each enabled feature contributes 1/3 of the total score.
    /// </summary>
    internal static double ComputeHttpSecurity(bool https, bool cors, bool antiforgery)
    {
        int checks = 3, passed = 0;
        if (https) passed++;
        if (cors) passed++;
        if (antiforgery) passed++;
        return (double)passed / checks;
    }

    private static OwaspCoverage BuildOwaspCoverage(
        double secretHygiene, double authPatterns, double inputValidation,
        int sqlInjectionFindings, double httpSecurity, double dataProtection)
    {
        // CryptographicFailures: based on secret hygiene and data protection practices
        double cryptoFailures = (secretHygiene * 0.5 + dataProtection * 0.5);

        // InsecureDesign: based on input validation and auth patterns
        double insecureDesign = (inputValidation * 0.5 + authPatterns * 0.5);

        // LoggingMonitoringFailures: projects with structured logging score well
        // (secretHygiene high means good practices, httpSecurity indicates infra maturity)
        double loggingMonitoring = (secretHygiene * 0.4 + httpSecurity * 0.3 + authPatterns * 0.3);

        // ServerSideRequestForgery: input validation and auth are primary mitigations
        double ssrf = (inputValidation * 0.6 + authPatterns * 0.4);

        return new OwaspCoverage
        {
            BrokenAccessControl = authPatterns,
            CryptographicFailures = cryptoFailures,
            Injection = sqlInjectionFindings == 0 ? 1.0 : Math.Max(0, 1.0 - sqlInjectionFindings * 0.2),
            InsecureDesign = insecureDesign,
            SecurityMisconfiguration = httpSecurity,
            VulnerableComponents = 1.0, // from DependencyHealth
            AuthenticationFailures = authPatterns,
            DataIntegrityFailures = dataProtection,
            LoggingMonitoringFailures = loggingMonitoring,
            ServerSideRequestForgery = ssrf
        };
    }

    private static int GetLineNumber(string content, int charIndex)
    {
        int line = 1;
        for (int i = 0; i < charIndex && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }

    private static List<string> GetSourceFiles(string path) =>
        Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                return !rel.Contains("/obj/") && !rel.StartsWith("obj/")
                    && !rel.Contains("/bin/") && !rel.StartsWith("bin/")
                    && !rel.Contains("benchmarks/")
                    && !rel.Contains(".claude/worktrees/");
            })
            .ToList();
}
