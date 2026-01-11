namespace LawAfrica.API.Services.Emails
{
    public class FileEmailTemplateStore : IEmailTemplateStore
    {
        private readonly IWebHostEnvironment _env;

        public FileEmailTemplateStore(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string TemplatesDir => Path.Combine(_env.ContentRootPath, "EmailTemplates");

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        {
            if (!Directory.Exists(TemplatesDir))
                return Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());

            var files = Directory.GetFiles(TemplatesDir, "*.html", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(x => !string.Equals(x, "_layout", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x)
                .ToList();

            return Task.FromResult((IReadOnlyList<string>)files);
        }

        public async Task<string> GetTemplateAsync(string name, CancellationToken ct = default)
        {
            var path = Path.Combine(TemplatesDir, $"{name}.html");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Email template not found: {name}", path);

            return await File.ReadAllTextAsync(path, ct);
        }

        public async Task<string> GetLayoutAsync(CancellationToken ct = default)
        {
            var path = Path.Combine(TemplatesDir, "_layout.html");
            if (!File.Exists(path))
                throw new FileNotFoundException("Email layout not found: _layout.html", path);

            return await File.ReadAllTextAsync(path, ct);
        }
    }
}
