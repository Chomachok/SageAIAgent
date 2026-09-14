using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Sage.Infrastructure.Plugins;

public class FileSystemPlugin
{
    private readonly ILogger<FileSystemPlugin>? _logger;
    private readonly string _rootPath;

    private readonly ConcurrentDictionary<string, FileCacheEntry> _fileCache = new();

    private const long MaxReadSize = 20 * 1024; // 20 KB
    private const int MaxSearchResults = 50;
    private const int MaxTreeDepth = 6;

    private static readonly string[] DefaultExcludes =
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea",
        "dist", "build", "out", "target", ".next", "__pycache__"
    };

    private static readonly string[] BlockedExtensions =
    {
        ".exe", ".dll", ".pdb", ".so", ".dylib",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp",
        ".zip", ".tar", ".gz", ".7z", ".rar",
        ".mp3", ".mp4", ".avi", ".mov", ".wav",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".bin", ".iso", ".dmg"
    };

    public FileSystemPlugin(ILogger<FileSystemPlugin>? logger, string? rootPath = ".")
    {
        _logger = logger;
        _rootPath = Path.GetFullPath(rootPath ?? Directory.GetCurrentDirectory());
    }
    
    [KernelFunction("read_file")]
    [System.ComponentModel.Description(
        "Reads the contents of a text file. Optional startLine/endLine (1-based) to read a range. Files over 20KB are truncated.")]
    public async Task<string> ReadFileAsync(
        [System.ComponentModel.Description("Path to the file.")] string path,
        [System.ComponentModel.Description("Optional: first line to read (1-based).")]
        int? startLine = null,
        [System.ComponentModel.Description("Optional: last line to read (1-based, inclusive).")]
        int? endLine = null)
    {
        try
        {
            var fullPath = ResolveAndValidate(path);

            if (!File.Exists(fullPath))
                return $"Error: file not found: {path}";

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (BlockedExtensions.Contains(ext))
                return $"Error: reading binary files ({ext}) is not allowed.";

            var content = await GetCachedContentAsync(fullPath);

            if (startLine.HasValue || endLine.HasValue)
            {
                var lines = content.Split('\n');
                var from = Math.Max(1, startLine ?? 1);
                var to = Math.Min(lines.Length, endLine ?? lines.Length);

                if (from > to)
                    return $"Error: invalid range (startLine={from} > endLine={to}).";

                var slice = string.Join('\n', lines.Skip(from - 1).Take(to - from + 1));
                return $"[Lines {from}-{to} of {lines.Length}]\n{slice}";
            }

            if (content.Length > MaxReadSize)
            {
                return content[..(int)MaxReadSize] +
                       $"\n\n[... file truncated, total {content.Length} chars ...]";
            }

            return content;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Access denied: {Path}", path);
            return $"Error: access denied.";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading file: {Path}", path);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction("list_files")]
    [System.ComponentModel.Description("Lists files and directories in a given folder (flat list).")]
    public string ListFiles(
        [System.ComponentModel.Description("The path to the directory to list.")]
        string path = "")
    {
        try
        {
            var targetPath = string.IsNullOrWhiteSpace(path) ? _rootPath : path;
            var fullPath = ResolveAndValidate(targetPath);

            if (!Directory.Exists(fullPath))
                return $"Error: directory not found: {path}";

            var entries = Directory.GetFileSystemEntries(fullPath);
            var result = entries
                .Select(e =>
                {
                    var name = Path.GetFileName(e);
                    var isDir = Directory.Exists(e);
                    return isDir ? $"{name}/" : name;
                });

            return string.Join("\n", result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error listing directory: {Path}", path);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction("write_file")]
    [System.ComponentModel.Description("Writes content to a file. Creates directories if needed.")]
    public async Task<string> WriteFileAsync(
        [System.ComponentModel.Description("The full path to the file to write.")]
        string path,
        [System.ComponentModel.Description("The content to write.")]
        string content)
    {
        try
        {
            var fullPath = ResolveAndValidate(path);

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);

            _fileCache.TryRemove(fullPath, out _);

            return $"OK: wrote {content.Length} chars to {path}";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error writing file: {Path}", path);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction("get_file_tree")]
    [System.ComponentModel.Description(
        "Returns a tree view of a directory, excluding common build/cache folders (bin, obj, node_modules, .git, etc).")]
    public string GetFileTree(
        [System.ComponentModel.Description("Root directory path.")]
        string path = "",
        [System.ComponentModel.Description("Max depth (default 3, max 6).")]
        int maxDepth = 3)
    {
        try
        {
            maxDepth = Math.Clamp(maxDepth, 1, MaxTreeDepth);

            var targetPath = string.IsNullOrWhiteSpace(path) ? _rootPath : path;
            var fullPath = ResolveAndValidate(targetPath);

            if (!Directory.Exists(fullPath))
                return $"Error: directory not found: {path}";

            var sb = new StringBuilder();
            sb.AppendLine(Path.GetFileName(fullPath) + "/");
            BuildTree(fullPath, sb, "", 1, maxDepth);

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error building tree: {Path}", path);
            return $"Error: {ex.Message}";
        }
    }

    private void BuildTree(string dir, StringBuilder sb, string indent, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        var entries = Directory.GetFileSystemEntries(dir)
            .OrderBy(e => Directory.Exists(e) ? 0 : 1) // папки первыми
            .ThenBy(e => Path.GetFileName(e))
            .ToList();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var name = Path.GetFileName(entry);
            var isLast = i == entries.Count - 1;
            var isDir = Directory.Exists(entry);

            if (isDir && DefaultExcludes.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            var connector = isLast ? "└── " : "├── ";
            sb.AppendLine($"{indent}{connector}{name}{(isDir ? "/" : "")}");

            if (isDir)
            {
                var newIndent = indent + (isLast ? "    " : "│   ");
                BuildTree(entry, sb, newIndent, depth + 1, maxDepth);
            }
        }
    }
    
    [KernelFunction("search_in_files")]
    [System.ComponentModel.Description(
        "Searches for a text pattern across files. Returns matching lines with file paths and line numbers. " +
        "Use this instead of reading many files one by one.")]
    public string SearchInFiles(
        [System.ComponentModel.Description("Text or regex pattern to search for.")]
        string pattern,
        [System.ComponentModel.Description("Directory to search in (default: project root).")]
        string path = "",
        [System.ComponentModel.Description("Optional file glob filter, e.g. '*.cs' or '*.py'.")]
        string? filePattern = null,
        [System.ComponentModel.Description("Treat pattern as regex (default: false = literal text).")]
        bool useRegex = false)
    {
        try
        {
            var targetPath = string.IsNullOrWhiteSpace(path) ? _rootPath : path;
            var fullPath = ResolveAndValidate(targetPath);

            if (!Directory.Exists(fullPath))
                return $"Error: directory not found: {path}";

            var results = new List<string>();
            var searchPattern = filePattern ?? "*.*";

            Regex? regex = null;
            if (useRegex)
            {
                try { regex = new Regex(pattern, RegexOptions.Compiled); }
                catch (Exception ex) { return $"Error: invalid regex: {ex.Message}"; }
            }

            foreach (var file in EnumerateFilesSafe(fullPath, searchPattern))
            {
                if (results.Count >= MaxSearchResults) break;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (BlockedExtensions.Contains(ext)) continue;

                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (results.Count >= MaxSearchResults) break;

                    var isMatch = regex?.IsMatch(lines[i])
                                  ?? lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase);

                    if (isMatch)
                    {
                        var relative = Path.GetRelativePath(_rootPath, file);
                        var preview = lines[i].Trim();
                        if (preview.Length > 150) preview = preview[..150] + "…";
                        results.Add($"{relative}:{i + 1}: {preview}");
                    }
                }
            }

            if (results.Count == 0)
                return $"No matches for '{pattern}' in {path}";

            var header = results.Count >= MaxSearchResults
                ? $"[showing first {MaxSearchResults} matches]"
                : $"[{results.Count} matches]";

            return header + "\n" + string.Join("\n", results);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error searching: {Pattern}", pattern);
            return $"Error: {ex.Message}";
        }
    }

    private IEnumerable<string> EnumerateFilesSafe(string dir, string pattern)
    {
        var queue = new Queue<string>();
        queue.Enqueue(dir);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current, pattern); }
            catch { continue; }

            foreach (var f in files)
                yield return f;

            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(current); }
            catch { continue; }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (DefaultExcludes.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                queue.Enqueue(sub);
            }
        }
    }


    private string ResolveAndValidate(string path)
    {
        var fullPath = Path.GetFullPath(path, _rootPath);

        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Access to path '{path}' is not allowed.");

        return fullPath;
    }

    private async Task<string> GetCachedContentAsync(string fullPath)
    {
        var info = new FileInfo(fullPath);
        var signature = (info.LastWriteTimeUtc, info.Length);

        if (_fileCache.TryGetValue(fullPath, out var cached) &&
            cached.LastWriteTimeUtc == signature.LastWriteTimeUtc &&
            cached.Length == signature.Length)
        {
            return cached.Content;
        }

        var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        _fileCache[fullPath] = new FileCacheEntry
        {
            LastWriteTimeUtc = signature.LastWriteTimeUtc,
            Length = signature.Length,
            Content = content
        };
        return content;
    }

    private sealed class FileCacheEntry
    {
        public DateTime LastWriteTimeUtc { get; init; }
        public long Length { get; init; }
        public string Content { get; init; } = "";
    }
}