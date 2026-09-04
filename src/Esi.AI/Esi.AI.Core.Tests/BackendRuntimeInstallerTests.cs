using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class BackendRuntimeInstallerTests
{
    [TestMethod]
    public async Task InstallAsync_VerifiedPackage_ActivatesRuntimeFiles()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-runtime-{Guid.NewGuid():N}");
        var archive = CreateArchive("libllama.so", "libggml.so", "libggml-base.so", "libggml-sycl.so");
        var package = CreatePackage(Convert.ToHexString(SHA256.HashData(archive)));
        var installer = CreateInstaller(applicationDirectory, archive, package);

        try
        {
            var result = await installer.InstallAsync(new BackendRuntimeInstallRequest(package.Id));

            Assert.IsTrue(result.IsInstalled);
            Assert.AreEqual(BackendRuntimeState.Installed, result.State);
            Assert.IsTrue(File.Exists(Path.Combine(applicationDirectory, "runtimes", "linux-x64", "native", "sycl", "libggml-sycl.so")));
        }
        finally
        {
            DeleteDirectory(applicationDirectory);
        }
    }

    [TestMethod]
    public async Task InstallAsync_InvalidHash_LeavesRuntimeInactive()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-runtime-{Guid.NewGuid():N}");
        var archive = CreateArchive("libllama.so", "libggml.so", "libggml-base.so", "libggml-sycl.so");
        var package = CreatePackage(new string('0', 64));
        var installer = CreateInstaller(applicationDirectory, archive, package);

        try
        {
            var result = await installer.InstallAsync(new BackendRuntimeInstallRequest(package.Id));

            Assert.IsFalse(result.IsInstalled);
            StringAssert.Contains(result.Message, "SHA-256 verification failed");
            Assert.IsFalse(Directory.Exists(Path.Combine(applicationDirectory, "runtimes", "linux-x64", "native", "sycl")));
        }
        finally
        {
            DeleteDirectory(applicationDirectory);
        }
    }

    [TestMethod]
    public async Task InstallAsync_PathTraversalArchive_LeavesRuntimeInactive()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-runtime-{Guid.NewGuid():N}");
        var archive = CreateArchive("../../outside.txt");
        var package = CreatePackage(Convert.ToHexString(SHA256.HashData(archive)));
        var installer = CreateInstaller(applicationDirectory, archive, package);
        var outsidePath = Path.Combine(Path.GetDirectoryName(applicationDirectory)!, "outside.txt");

        try
        {
            var result = await installer.InstallAsync(new BackendRuntimeInstallRequest(package.Id));

            Assert.IsFalse(result.IsInstalled);
            StringAssert.Contains(result.Message, "unsafe path");
            Assert.IsFalse(File.Exists(outsidePath));
        }
        finally
        {
            DeleteDirectory(applicationDirectory);
            if (File.Exists(outsidePath))
                File.Delete(outsidePath);
        }
    }

    [TestMethod]
    public async Task InstallAsync_LocalRuntimeDirectory_ActivatesRuntimeFiles()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-runtime-{Guid.NewGuid():N}");
        var localRuntimeDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localRuntimeDirectory);
        foreach (var fileName in new[] { "libllama.so", "libggml.so", "libggml-base.so", "libggml-sycl.so", "libmtmd.so" })
            File.WriteAllText(Path.Combine(localRuntimeDirectory, fileName), "native runtime");

        var package = CreatePackage(string.Empty, localRuntimeDirectory, ["libllama.so", "libggml.so", "libggml-base.so", "libggml-sycl.so", "libmtmd.so"]);
        var installer = CreateInstaller(applicationDirectory, [], package, allowLocalPackages: true);

        try
        {
            var result = await installer.InstallAsync(new BackendRuntimeInstallRequest(package.Id));

            Assert.IsTrue(result.IsInstalled);
            Assert.IsTrue(File.Exists(Path.Combine(applicationDirectory, "runtimes", "linux-x64", "native", "sycl", "libmtmd.so")));
        }
        finally
        {
            DeleteDirectory(applicationDirectory);
            DeleteDirectory(localRuntimeDirectory);
        }
    }

    [TestMethod]
    public async Task InstallAsync_LocalRuntimeDirectoryMissingFile_LeavesRuntimeInactive()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-runtime-{Guid.NewGuid():N}");
        var localRuntimeDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localRuntimeDirectory);
        foreach (var fileName in new[] { "libllama.so", "libggml.so", "libggml-base.so", "libggml-sycl.so" })
            File.WriteAllText(Path.Combine(localRuntimeDirectory, fileName), "native runtime");

        var package = CreatePackage(string.Empty, localRuntimeDirectory, ["libllama.so", "libggml.so", "libggml-base.so", "libggml-sycl.so", "libmtmd.so"]);
        var installer = CreateInstaller(applicationDirectory, [], package, allowLocalPackages: true);

        try
        {
            var result = await installer.InstallAsync(new BackendRuntimeInstallRequest(package.Id));

            Assert.IsFalse(result.IsInstalled);
            StringAssert.Contains(result.Message, "libmtmd.so");
        }
        finally
        {
            DeleteDirectory(applicationDirectory);
            DeleteDirectory(localRuntimeDirectory);
        }
    }

    [TestMethod]
    public async Task InstallAsync_LocalRuntimeDirectory_WhenDisabled_LeavesRuntimeInactive()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-runtime-{Guid.NewGuid():N}");
        var localRuntimeDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localRuntimeDirectory);
        var package = CreatePackage(string.Empty, localRuntimeDirectory, ["libllama.so"]);
        var installer = CreateInstaller(applicationDirectory, [], package);

        try
        {
            var result = await installer.InstallAsync(new BackendRuntimeInstallRequest(package.Id));

            Assert.IsFalse(result.IsInstalled);
            StringAssert.Contains(result.Message, "disabled");
        }
        finally
        {
            DeleteDirectory(applicationDirectory);
            DeleteDirectory(localRuntimeDirectory);
        }
    }

    private static BackendRuntimeInstaller CreateInstaller(string applicationDirectory, byte[] archive, BackendRuntimePackage package, bool allowLocalPackages = false)
    {
        var client = new HttpClient(new ArchiveHandler(archive));
        var options = Options.Create(new BackendRuntimeOptions { Packages = [package], AllowLocalPackages = allowLocalPackages });
        return new BackendRuntimeInstaller(client, options, applicationDirectory);
    }

    private static BackendRuntimePackage CreatePackage(string sha256, string? localPath = null, IReadOnlyList<string>? requiredFiles = null) =>
        new("llama-sycl-linux-x64", ConfigurationBackend.Llama, "sycl", "linux-x64", "1.0.0",
            "https://example.test/llama-sycl.zip", sha256, requiredFiles ?? [], "Intel GPU and Level Zero", true, localPath);

    private static byte[] CreateArchive(params string[] fileNames)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var fileName in fileNames)
            {
                var entry = archive.CreateEntry(fileName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("native runtime");
            }
        }

        return stream.ToArray();
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive)
            });
    }
}
