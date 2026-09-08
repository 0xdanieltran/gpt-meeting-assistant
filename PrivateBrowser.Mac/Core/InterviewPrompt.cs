using System.Text;

namespace PrivateBrowser.Mac
{
    public static class InterviewPrompt
    {
        public const int RecentTranscriptBlockCount = 3;

        public static string Build(
            string transcript
        )
        {
            StringBuilder builder =
                new StringBuilder();

            builder.AppendLine(
                "You are assisting a candidate during a live interview."
            );
            builder.AppendLine();
            builder.AppendLine(
                "The excerpts below are the most recent spoken blocks from the conversation. Use them as context, then answer the interviewer's latest question as the candidate would in the interview."
            );
            builder.AppendLine();
            builder.AppendLine("Instructions:");
            builder.AppendLine(
                "1. Identify the interviewer's most recent question from the transcript."
            );
            builder.AppendLine(
                "2. Provide a clear, concise, interview-ready spoken answer to that question."
            );
            builder.AppendLine(
                "3. Use earlier excerpts only as supporting context."
            );
            builder.AppendLine(
                "4. Do not recap or quote the transcript unless a short reference is necessary."
            );
            builder.AppendLine(
                "5. If the last block is from Me (the candidate), ignore it. Answer the interviewer's most recent question from the remaining transcript."
            );
            builder.AppendLine();
            builder.AppendLine("Transcript:");
            builder.AppendLine("-----");
            builder.AppendLine(transcript.Trim());
            builder.Append("-----");
            return builder.ToString();
        }

        public static string FormatCaptionLine(
            CaptionItem item
        )
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Text))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(item.Speaker)
                ? item.Text
                : $"{item.Speaker}: {item.Text}";
        }
    }
}
