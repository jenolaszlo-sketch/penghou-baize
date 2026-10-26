using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

if (args.Length is < 4 or > 7 || args[0] != "claim") return 64;

var databasePath = args[1];
var authorization = LiveAuthorizations.Read(args[2]);
LiveAuthorizationAuthentication.Verify(authorization);
var correlationId = args[3];
string? dispatchReceiptPath = null;
var crashAfterClaim = false;
var optionalArguments = args.Skip(4).ToArray();
if ((optionalArguments.Length is 2 or 3) && optionalArguments[0] != "crash")
{
    var readyPath = optionalArguments[0];
    var startPath = optionalArguments[1];
    if (optionalArguments.Length == 3) dispatchReceiptPath = optionalArguments[2];
    File.WriteAllText(readyPath, "ready");
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
    while (!File.Exists(startPath) && DateTime.UtcNow < deadline)
        Thread.Sleep(TimeSpan.FromMilliseconds(5));
    if (!File.Exists(startPath)) return 65;
}
else if (optionalArguments.Length == 1)
{
    if (optionalArguments[0] == "crash") crashAfterClaim = true;
    else dispatchReceiptPath = optionalArguments[0];
}
else if (optionalArguments.Length == 2 && optionalArguments[0] == "crash")
{
    crashAfterClaim = true;
    dispatchReceiptPath = optionalArguments[1];
}
else if (optionalArguments.Length != 0) return 64;

try
{
    var registry = AuthorizationConsumptionRegistry.OpenAt(databasePath, authorization.ConsumptionAuthorityId);
    registry.Claim(authorization, CanonicalJson.Identity(authorization), authorization.PlanHash,
        authorization.TransportProfileIdentity, correlationId, DateTimeOffset.UtcNow);
    Console.WriteLine("claimed");
    if (crashAfterClaim) Environment.Exit(77);
    if (dispatchReceiptPath is not null) File.WriteAllText(dispatchReceiptPath, "admitted");
    Console.WriteLine("simulated-dispatch-admitted");
    return 0;
}
catch (ExecutionException exception) when (exception.Code == "live-authorization-already-consumed")
{
    Console.WriteLine(exception.Code);
    return 2;
}
catch (ExecutionException exception)
{
    Console.WriteLine(exception.Code);
    return 3;
}
