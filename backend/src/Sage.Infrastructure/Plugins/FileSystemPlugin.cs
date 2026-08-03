using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Sage.Infrastructure.Plugins;

public class FileSystemPlugin(ILogger<FileSystemPlugin>? logger = null, string? rootPath = null)
{
    private readonly string _rootPath = rootPath;

    [KernelFunction("read_file")]
    [Description("Reads the contents of a file and returns it as a string.")]
    public async Task<string> ReadFileAsync(
        [Description("The full path to the file to read.")] string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var rootFull = Path.GetFullPath(_rootPath);
            if (!fullPath.StartsWith(rootFull))
            {
                throw new UnauthorizedAccessException($"Access to path '{path}' is not allowed.");
            }

            if (!File.Exists(fullPath))
            {
                return $"File not found: {path}";
            }

            var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
            logger?.LogInformation("Read file: {Path}, size: {Size} bytes", path, content.Length);
            return content;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error reading file: {Path}", path);
            return $"Error reading file: {ex.Message}";
        }
    }

    [KernelFunction("list_files")]
    [Description("Lists files and directories in a given folder.")]
    public string ListFiles(
        [Description("The path to the directory to list.")] string path = null)
    {
        try
        {
            var targetPath = string.IsNullOrEmpty(path) ? _rootPath : path;
            var fullPath = Path.GetFullPath(targetPath);
            var rootFull = Path.GetFullPath(_rootPath);
            if (!fullPath.StartsWith(rootFull))
            {
                throw new UnauthorizedAccessException($"Access to path '{targetPath}' is not allowed.");
            }

            if (!Directory.Exists(fullPath))
            {
                return $"Directory not found: {targetPath}";
            }

            var entries = Directory.GetFileSystemEntries(fullPath);
            return string.Join("\n", entries.Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : "")));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error listing directory: {Path}", path);
            return $"Error listing directory: {ex.Message}";
        }
    }
    
    [KernelFunction("write_file")]
    [Description("Writes content to a file. Overwrites existing file.")]
    public async Task<string> WriteFileAsync(
        [Description("The full path to the file to write.")] string path,
        [Description("The content to write to the file.")] string content)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var rootFull = Path.GetFullPath(_rootPath);
            if (!fullPath.StartsWith(rootFull))
            {
                throw new UnauthorizedAccessException($"Access to path '{path}' is not allowed.");
            }

            // Создаём директорию, если её нет
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
            logger.LogInformation("Written file: {Path}, size: {Size} bytes", path, content.Length);
            return $"File written successfully: {path}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error writing file: {Path}", path);
            return $"Error writing file: {ex.Message}";
        }
    }
}