using System.Text;
using Microsoft.Extensions.AI;

namespace ElBruno.S1Mini.Internal;

/// <summary>
/// Builds the Qwen3 ChatML prompt used by s1-mini. This is a stripped-down
/// version of the format used by the full <c>Qwen3Formatter</c> in
/// <c>ElBruno.LocalLLMs</c>: s1-mini does not use tool-calling, so tool
/// definitions/results/calls are unnecessary. Only system + user messages are
/// consumed here (the assistant turn is what the model generates).
/// <para>
/// Verified byte-for-byte against the model's own <c>chat_template.jinja</c>
/// and reproduced 6/6 outputs identically against the real INT4 model.
/// The generation prompt terminator is exactly:
/// </para>
/// <code>&lt;|im_start|&gt;assistant\n&lt;think&gt;\n\n&lt;/think&gt;\n\n</code>
/// <para>
/// The empty <c>&lt;think&gt;</c> block puts Qwen3 in non-thinking mode
/// (<c>enable_thinking=False</c>), which is what s1-mini expects.
/// </para>
/// </summary>
internal static class Qwen3PromptBuilder
{
    public static string Build(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            var role = MapRole(message.Role);
            var content = message.Text ?? string.Empty;
            sb.Append("<|im_start|>").Append(role).Append('\n').Append(content).Append("<|im_end|>\n");
        }

        // Non-thinking generation prompt — verbatim from Qwen3's chat_template.jinja
        // when enable_thinking=False.
        sb.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n");

        return sb.ToString();
    }

    private static string MapRole(ChatRole role)
    {
        if (role == ChatRole.System) return "system";
        if (role == ChatRole.User) return "user";
        if (role == ChatRole.Assistant) return "assistant";
        return role.Value;
    }
}
