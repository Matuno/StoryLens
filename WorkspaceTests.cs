using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace StoryLens;

internal sealed class WorkspaceTests : IDisposable
{
    private const int MaximumTestRequestCharacters = 32 * 1024;

    private readonly IFramework framework;
    private readonly Func<object> capture;
    private readonly ICallGateProvider<string> listTests;
    private readonly ICallGateProvider<string, string> runTests;
    private readonly string assemblySha256;
    private bool disposed;

    public WorkspaceTests(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        Func<object> capture)
    {
        this.framework = framework;
        this.capture = capture;
        this.assemblySha256 = GetAssemblySha256(pluginInterface.AssemblyLocation);
        this.listTests = pluginInterface.GetIpcProvider<string>(
            "DalamudWorkspace.ListTests.v1.StoryLens");
        this.runTests = pluginInterface.GetIpcProvider<string, string>(
            "DalamudWorkspace.RunTests.v1.StoryLens");

        this.listTests.RegisterFunc(() => JsonSerializer.Serialize(new[]
        {
            new { name = "smoke", readOnly = true, description = "Validates quest catalog and redacted observation invariants." },
        }));
        try
        {
            this.runTests.RegisterFunc(this.RunWorkspaceTest);
        }
        catch
        {
            this.listTests.UnregisterFunc();
            throw;
        }
    }

    public void Dispose()
    {
        if (this.disposed)
            return;
        this.disposed = true;
        this.runTests.UnregisterFunc();
        this.listTests.UnregisterFunc();
    }

    private string RunWorkspaceTest(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson) || requestJson.Length > MaximumTestRequestCharacters)
            return TestFailure("invalid-request");

        try
        {
            using var request = JsonDocument.Parse(requestJson);
            var name = request.RootElement.ValueKind == JsonValueKind.Object &&
                       request.RootElement.TryGetProperty("name", out var value) &&
                       value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            if (!string.Equals(name, "smoke", StringComparison.Ordinal))
                return TestFailure("unknown-test");
        }
        catch (JsonException)
        {
            return TestFailure("invalid-request");
        }

        string Capture()
        {
            if (this.disposed)
                return TestFailure("unloaded");
            var result = JsonSerializer.SerializeToElement(this.capture());
            return JsonSerializer.Serialize(new
            {
                ok = result.GetProperty("ok").GetBoolean(),
                test = "smoke", result,
                assemblySha256 = this.assemblySha256,
                assemblyMvid = typeof(Plugin).Assembly.ManifestModule.ModuleVersionId,
            });
        }

        // Direct IPC callers may already be on the framework thread. Never queue
        // back to that same thread and synchronously wait for the queued work.
        try
        {
            if (this.framework.IsInFrameworkUpdateThread)
                return Capture();
            return this.framework.RunOnFrameworkThread(Capture)
                .WaitAsync(TimeSpan.FromSeconds(3))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            return TestFailure("capture-failed");
        }
    }

    private static string TestFailure(string error)
        => JsonSerializer.Serialize(new { ok = false, error });

    private static string GetAssemblySha256(FileInfo assemblyLocation)
    {
        assemblyLocation.Refresh();
        if (!assemblyLocation.Exists)
            throw new InvalidOperationException("Dalamud's registered plugin DLL does not exist.");
        if ((assemblyLocation.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The registered plugin DLL may not be a reparse point.");

        using var stream = new FileStream(
            assemblyLocation.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using (var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            if (!peReader.HasMetadata)
                throw new InvalidOperationException("The registered plugin DLL has no managed metadata.");
            var metadata = peReader.GetMetadataReader();
            var fileMvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            if (fileMvid != typeof(Plugin).Assembly.ManifestModule.ModuleVersionId)
                throw new InvalidOperationException("The registered plugin DLL does not match the loaded assembly.");
        }

        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
