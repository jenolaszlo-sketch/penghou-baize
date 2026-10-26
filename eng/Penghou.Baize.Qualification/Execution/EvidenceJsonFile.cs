using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Execution;

internal static class EvidenceJsonFile
{
    public static void WriteCanonical(string path, string json)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ExecutionException("evidence-output-path-invalid");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, CanonicalJson.CanonicalizeEvidence(json));
    }
}
