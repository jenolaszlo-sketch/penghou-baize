using System.Text;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Execution;

internal static class AuthorizationConsumptionRegistryCommand
{
    public static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length != 3 || args[0] != "provision" || args[1] != "--path"
                || string.IsNullOrWhiteSpace(args[2]))
                throw new ExecutionException("invalid-authorization-registry-options");
            var authorityId = AuthorizationConsumptionRegistry.Provision(args[2]);
            output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
            {
                kind = "authorization-consumption-registry-provisioned",
                schemaVersion = "1.0",
                authorityId,
                commandCompleted = true,
                executionAuthorized = false
            })));
            return 0;
        }
        catch (ExecutionException exception)
        {
            error.WriteLine(exception.Code);
            output.WriteLine(Encoding.UTF8.GetString(CanonicalJson.Write(new
            {
                kind = "command-failed",
                schemaVersion = "1.0",
                code = exception.Code,
                exitCode = 2,
                commandCompleted = false,
                executionAuthorized = false
            })));
            return 2;
        }
    }
}
