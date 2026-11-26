using System.Text;
using BuildMind.Application.Features.PlanEditor.Integration;

namespace BuildMind.Application.ContractTests.Integration;

internal static class CommandFingerprintContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("command fingerprint is deterministic for identical canonical bytes", () =>
        {
            var bytes = Encoding.UTF8.GetBytes("approved-test-shape:v1|project|plan|command");
            var first = CommandFingerprint.Compute(bytes);
            var second = CommandFingerprint.Compute(bytes);
            Check(first.IsValid && first.Matches(second) && first == second,
                "Identical canonical bytes produced different fingerprints.");
        });

        run("command fingerprint changes when canonical bytes change", () =>
        {
            var first = CommandFingerprint.Compute(Encoding.UTF8.GetBytes("command:move|x:1|y:2"));
            var second = CommandFingerprint.Compute(Encoding.UTF8.GetBytes("command:move|x:1|y:3"));
            Check(!first.Matches(second) && first != second,
                "Different canonical bytes produced a matching fingerprint.");
        });

        run("command fingerprint uses stable lowercase SHA-256 representation", () =>
        {
            var fingerprint = CommandFingerprint.Compute(Encoding.UTF8.GetBytes("canonical-command"));
            Check(fingerprint.Value is { Length: 71 } &&
                fingerprint.Value.StartsWith("sha256:", StringComparison.Ordinal) &&
                fingerprint.Value == fingerprint.Value.ToLowerInvariant(),
                "Fingerprint representation is not the proposed stable SHA-256 form.");
        });

        run("command fingerprint rejects an empty canonical representation", () =>
        {
            CheckThrows<ArgumentException>(() => CommandFingerprint.Compute(ReadOnlySpan<byte>.Empty));
        });

        run("command fingerprint is independent of later source-buffer mutation", () =>
        {
            var bytes = Encoding.UTF8.GetBytes("immutable-canonical-command");
            var expected = CommandFingerprint.Compute(bytes);
            bytes[0] ^= 0xff;
            Check(expected.IsValid && expected.Value == CommandFingerprint
                .Compute(Encoding.UTF8.GetBytes("immutable-canonical-command")).Value,
                "Fingerprint retained mutable source-buffer state.");
        });

        run("default and mismatched command fingerprints never reconcile", () =>
        {
            var valid = CommandFingerprint.Compute(Encoding.UTF8.GetBytes("command-a"));
            var different = CommandFingerprint.Compute(Encoding.UTF8.GetBytes("command-b"));
            Check(!default(CommandFingerprint).IsValid &&
                !default(CommandFingerprint).Matches(valid) &&
                !valid.Matches(default) &&
                !valid.Matches(different),
                "An invalid or mismatched fingerprint was accepted as the same command.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
