using Microsoft.Extensions.AI;

namespace Usine.Core.Modeles;

public sealed class ConsommationGemini(IChatClient client) : DelegatingChatClient(client)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var reponse = await base.GetResponseAsync(messages, options, cancellationToken);
        if (reponse is { Usage: { CachedInputTokenCount: null } usage, RawRepresentation: Google.GenAI.Types.GenerateContentResponse { UsageMetadata: not null } })
        {
            usage.CachedInputTokenCount = 0;
        }

        return reponse;
    }
}
