using System.Collections.Concurrent;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Application.Services;

public class InMemoryChatHistoryStore : IChatHistoryStore
{
    private readonly ConcurrentDictionary<(Guid AccountId, string ConversationId), List<AiChatMessageDto>> _store = new();
    private readonly int _maxHistoryPerConversation;

    public InMemoryChatHistoryStore(int maxHistoryPerConversation = 5)
    {
        _maxHistoryPerConversation = maxHistoryPerConversation;
    }

    public void AddMessage(Guid accountId, string conversationId, string sender, string text)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(text))
            return;

        var message = new AiChatMessageDto(sender, text, DateTime.UtcNow);
        var key = (accountId, conversationId);

        _store.AddOrUpdate(
            key,
            _ => new List<AiChatMessageDto> { message },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(message);
                    if (list.Count > _maxHistoryPerConversation)
                    {
                        list.RemoveRange(0, list.Count - _maxHistoryPerConversation);
                    }
                }
                return list;
            });
    }

    public List<AiChatMessageDto> GetHistory(Guid accountId, string conversationId, int maxCount = 5)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(conversationId) ||
            !_store.TryGetValue((accountId, conversationId), out var list))
        {
            return new List<AiChatMessageDto>();
        }

        lock (list)
        {
            return list.TakeLast(maxCount).ToList();
        }
    }

    public AiChatMessageDto? GetLastAssistantMessage(Guid accountId, string conversationId)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(conversationId) ||
            !_store.TryGetValue((accountId, conversationId), out var list))
        {
            return null;
        }

        lock (list)
        {
            return list.LastOrDefault(m => string.Equals(m.Sender, "assistant", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(m.Sender, "bot", StringComparison.OrdinalIgnoreCase));
        }
    }

    public void ClearHistory(Guid accountId, string conversationId)
    {
        if (accountId != Guid.Empty && !string.IsNullOrWhiteSpace(conversationId))
        {
            _store.TryRemove((accountId, conversationId), out _);
        }
    }

    public void ClearAccountHistory(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            return;
        }

        foreach (var key in _store.Keys.Where(key => key.AccountId == accountId))
        {
            _store.TryRemove(key, out _);
        }
    }
}
