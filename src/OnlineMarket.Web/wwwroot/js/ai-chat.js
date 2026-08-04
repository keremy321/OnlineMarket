/**
 * OnlineMarket AI Support Assistant JavaScript Module
 */
document.addEventListener('DOMContentLoaded', () => {
    const launcherBtn = document.getElementById('aiWidgetLauncher');
    const widgetBox = document.getElementById('aiWidgetBox');
    const closeBtn = document.getElementById('aiWidgetClose');
    const chatBody = document.getElementById('aiChatBody');
    const inputField = document.getElementById('aiInputField');
    const sendBtn = document.getElementById('aiSendBtn');
    const badge = document.getElementById('aiLauncherBadge');

    if (!launcherBtn || !widgetBox || !inputField || !sendBtn) {
        return;
    }

    const maxChatHistoryMessages = 5;
    let conversationId = null;
    let isPending = false;
    const chatHistory = [];

    // A page load starts a new conversation. Remove IDs persisted by older versions.
    try {
        localStorage.removeItem('ai_chat_conv_id');
    } catch {
        // Storage can be unavailable in privacy-restricted browser contexts.
    }

    // Toggle Chat Window
    launcherBtn.addEventListener('click', () => {
        const isActive = widgetBox.classList.toggle('active');
        if (isActive) {
            if (badge) badge.style.display = 'none';
            inputField.focus();
            scrollToBottom();
        }
    });

    if (closeBtn) {
        closeBtn.addEventListener('click', () => {
            widgetBox.classList.remove('active');
        });
    }

    // Handle Input keypress
    inputField.addEventListener('keypress', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    sendBtn.addEventListener('click', () => {
        sendMessage();
    });

    // Delegation for quick suggestion chips
    chatBody.addEventListener('click', (e) => {
        if (e.target && e.target.classList.contains('ai-chip')) {
            const queryText = e.target.textContent;
            if (queryText && !isPending) {
                inputField.value = queryText;
                sendMessage();
            }
        }
    });

    async function sendMessage() {
        const messageText = inputField.value.trim();
        if (!messageText || isPending) return;

        // Render User Message
        appendUserMessage(messageText);
        inputField.value = '';
        setPending(true);

        // Show typing indicator
        const typingElem = showTypingIndicator();
        scrollToBottom();

        try {
            const token = getAntiForgeryToken();
            const response = await fetch('/AiSupport/Chat', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    message: messageText,
                    conversationId: conversationId,
                    currentUrl: window.location.href,
                    history: chatHistory.slice(-maxChatHistoryMessages)
                })
            });

            removeTypingIndicator(typingElem);

            if (!response.ok) {
                appendBotMessage('Üzgünüm, şu anda yanıt oluşturulamadı. Lütfen tekrar deneyin.', null);
                return;
            }

            const data = await response.json();
            if (data.conversationId) {
                conversationId = data.conversationId;
            }

            chatHistory.push({ sender: 'user', text: messageText, timestampUtc: new Date().toISOString() });
            if (data.reply) {
                chatHistory.push({ sender: 'assistant', text: data.reply, timestampUtc: new Date().toISOString() });
            }
            trimChatHistory();

            appendBotMessage(data.reply, data.suggestedActions);
        } catch (err) {
            console.error('AI Chat Error:', err);
            removeTypingIndicator(typingElem);
            appendBotMessage('Bağlantı hatası oluştu. Lütfen internet bağlantınızı kontrol edin ve tekrar deneyin.', null);
        } finally {
            setPending(false);
            scrollToBottom();
        }
    }

    function appendUserMessage(text) {
        const timeStr = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        const messageDiv = document.createElement('div');
        messageDiv.className = 'ai-message user';
        messageDiv.innerHTML = `
            <div class="ai-message-bubble">${escapeHtml(text)}</div>
            <div class="ai-message-time">${timeStr}</div>
        `;
        chatBody.appendChild(messageDiv);
    }

    function appendBotMessage(text, suggestions) {
        const timeStr = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        const messageDiv = document.createElement('div');
        messageDiv.className = 'ai-message bot';

        let formattedText = formatMarkdownText(text);

        let html = `<div class="ai-message-bubble">${formattedText}</div>`;

        if (suggestions && suggestions.length > 0) {
            html += `<div class="ai-suggestions-wrapper">`;
            suggestions.forEach(s => {
                html += `<button type="button" class="ai-chip">${escapeHtml(s)}</button>`;
            });
            html += `</div>`;
        }

        html += `<div class="ai-message-time">${timeStr}</div>`;
        messageDiv.innerHTML = html;
        chatBody.appendChild(messageDiv);
    }

    function showTypingIndicator() {
        const typingDiv = document.createElement('div');
        typingDiv.className = 'ai-message bot typing';
        typingDiv.innerHTML = `
            <div class="ai-typing-indicator">
                <div class="ai-typing-dot"></div>
                <div class="ai-typing-dot"></div>
                <div class="ai-typing-dot"></div>
            </div>
        `;
        chatBody.appendChild(typingDiv);
        return typingDiv;
    }

    function removeTypingIndicator(elem) {
        if (elem && elem.parentNode) {
            elem.parentNode.removeChild(elem);
        }
    }

    function setPending(pending) {
        isPending = pending;
        sendBtn.disabled = pending;
        inputField.disabled = pending;
        if (!pending) inputField.focus();
    }

    function scrollToBottom() {
        chatBody.scrollTop = chatBody.scrollHeight;
    }

    function trimChatHistory() {
        if (chatHistory.length > maxChatHistoryMessages) {
            chatHistory.splice(0, chatHistory.length - maxChatHistoryMessages);
        }
    }

    function getAntiForgeryToken() {
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function formatMarkdownText(text) {
        if (!text) return '';
        let escaped = escapeHtml(text);
        
        // Bold: **text**
        escaped = escaped.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
        // Inline code: `text`
        escaped = escaped.replace(/`(.*?)`/g, '<code class="bg-light px-1 rounded text-primary">$1</code>');
        // Bullet points: \n- or \n•
        escaped = escaped.replace(/\n- /g, '<br/>• ');
        escaped = escaped.replace(/\n• /g, '<br/>• ');
        // Newlines
        escaped = escaped.replace(/\n/g, '<br/>');
        return escaped;
    }
});
