using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Application.Interfaces;

public interface IChatHistoryStore
{
    void AddMessage(Guid accountId, string conversationId, string sender, string text);
    List<AiChatMessageDto> GetHistory(Guid accountId, string conversationId, int maxCount = 5);
    AiChatMessageDto? GetLastAssistantMessage(Guid accountId, string conversationId);
    void ClearHistory(Guid accountId, string conversationId);
    void ClearAccountHistory(Guid accountId);
}
