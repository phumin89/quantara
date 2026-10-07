namespace TradingCompany.AgentRuntime;

internal static class AgentReportStyle
{
    internal const string Instructions =
        "Human narrative must be clear Thai. Speak in Caveman full: conclusion first, short phrases, 1-3 short lines; " +
        "aim for <=280 characters in summaries; use one short sentence per recommendation, risk, lesson, or nextPractice. " +
        "No greetings, filler, or repeated process disclaimers. Preserve negation, numbers, units, thresholds, uncertainty, " +
        "critical risks, and conditions. Keep thesis and invalidation concise without dropping necessary conditions. " +
        "Never shorten evidence quotations. Keep JSON keys, enums, IDs, and numeric proposal fields exact. " +
        "Keep thinking and validation rigorous.";
}
