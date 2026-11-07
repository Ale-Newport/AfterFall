namespace Afterfall.Core
{
    /// <summary>
    /// Outcome of a gameplay operation. Failures carry a stable machine-readable code
    /// (for tests and telemetry) and a human-readable explanation (for the UI), so the
    /// player always learns *why* something did not happen.
    /// </summary>
    public readonly struct OpResult
    {
        public readonly bool Ok;
        public readonly string Code;
        public readonly string Message;

        private OpResult(bool ok, string code, string message)
        {
            Ok = ok;
            Code = code;
            Message = message;
        }

        public static OpResult Success(string message = "") => new OpResult(true, "ok", message);
        public static OpResult Fail(string code, string message) => new OpResult(false, code, message);

        public override string ToString() => Ok ? "Ok" + (string.IsNullOrEmpty(Message) ? "" : ": " + Message) : $"Fail[{Code}]: {Message}";
    }
}
