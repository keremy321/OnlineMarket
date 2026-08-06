/**
 * OnlineMarket AI Support Assistant.
 *
 * Talks only to OnlineMarket.Web. Product cards are rendered from the structured
 * `products` array the server builds out of Recommendation.Api results; assistant text
 * is always inserted as plain text so provider output can never become markup.
 */
(function () {
    'use strict';

    var PLACEHOLDER_IMAGE = 'https://placehold.co/200x200?text=Urun';
    var DEFAULT_MAX_LENGTH = 500;

    function init() {
        var root = document.getElementById('aiWidgetRoot');
        var launcherBtn = document.getElementById('aiWidgetLauncher');
        var widgetBox = document.getElementById('aiWidgetBox');
        var closeBtn = document.getElementById('aiWidgetClose');
        var chatBody = document.getElementById('aiChatBody');
        var inputField = document.getElementById('aiInputField');
        var sendBtn = document.getElementById('aiSendBtn');
        var badge = document.getElementById('aiLauncherBadge');

        // The widget is intentionally absent on pages that do not host it.
        if (!root || !launcherBtn || !widgetBox || !chatBody || !inputField || !sendBtn) {
            return;
        }

        var chatUrl = root.getAttribute('data-chat-url') || '/AiSupport/Chat';
        var currentProductId = root.getAttribute('data-current-product-id') || null;
        var pageType = root.getAttribute('data-page-type') || null;
        var maxLength = parseInt(root.getAttribute('data-max-length'), 10);
        if (!maxLength || maxLength < 1) {
            maxLength = DEFAULT_MAX_LENGTH;
        }

        var maxChatHistoryMessages = 5;
        var conversationId = null;
        var isPending = false;
        var chatHistory = [];

        // A page load starts a new conversation. Remove ids persisted by older versions.
        try {
            localStorage.removeItem('ai_chat_conv_id');
        } catch (storageError) {
            // Storage can be unavailable in privacy-restricted browser contexts.
        }

        function setOpen(open) {
            widgetBox.classList.toggle('active', open);
            launcherBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
            launcherBtn.setAttribute(
                'aria-label',
                open ? 'AI destek asistanını kapat' : 'AI destek asistanını aç');
            if (open) {
                if (badge) {
                    badge.style.display = 'none';
                }
                inputField.focus();
                scrollToBottom();
            }
        }

        launcherBtn.addEventListener('click', function () {
            setOpen(!widgetBox.classList.contains('active'));
        });

        if (closeBtn) {
            closeBtn.addEventListener('click', function () {
                setOpen(false);
                launcherBtn.focus();
            });
        }

        root.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && widgetBox.classList.contains('active')) {
                setOpen(false);
                launcherBtn.focus();
            }
        });

        inputField.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });

        sendBtn.addEventListener('click', function () {
            sendMessage();
        });

        // Delegation for quick suggestion chips and product cards.
        chatBody.addEventListener('click', function (e) {
            var chip = e.target.closest ? e.target.closest('.ai-chip') : null;
            if (chip && !isPending) {
                inputField.value = chip.textContent || '';
                sendMessage();
            }
        });

        function sendMessage() {
            var messageText = (inputField.value || '').trim();
            if (!messageText || isPending) {
                return;
            }

            if (messageText.length > maxLength) {
                messageText = messageText.substring(0, maxLength);
            }

            appendUserMessage(messageText);
            inputField.value = '';
            setPending(true);

            var typingElem = showTypingIndicator();
            scrollToBottom();

            var payload = {
                message: messageText,
                conversationId: conversationId,
                currentUrl: window.location.pathname,
                history: chatHistory.slice(-maxChatHistoryMessages),
                currentProductId: currentProductId,
                pageType: pageType
            };

            fetch(chatUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                body: JSON.stringify(payload)
            }).then(function (response) {
                removeTypingIndicator(typingElem);
                if (!response.ok) {
                    appendBotMessage(
                        'Üzgünüm, şu anda yanıt oluşturulamadı. Lütfen tekrar deneyin.',
                        null,
                        null);
                    return null;
                }
                return response.json();
            }).then(function (data) {
                if (!data) {
                    return;
                }

                if (data.conversationId) {
                    conversationId = data.conversationId;
                }

                chatHistory.push({
                    sender: 'user',
                    text: messageText,
                    timestampUtc: new Date().toISOString()
                });
                if (data.reply) {
                    chatHistory.push({
                        sender: 'assistant',
                        text: data.reply,
                        timestampUtc: new Date().toISOString()
                    });
                }
                trimChatHistory();

                appendBotMessage(data.reply, data.suggestedActions, data.products);
            }).catch(function () {
                removeTypingIndicator(typingElem);
                appendBotMessage(
                    'Bağlantı hatası oluştu. Lütfen bağlantınızı kontrol edip tekrar deneyin.',
                    null,
                    null);
            }).then(function () {
                setPending(false);
                scrollToBottom();
            });
        }

        function appendUserMessage(text) {
            var messageDiv = document.createElement('div');
            messageDiv.className = 'ai-message user';
            messageDiv.appendChild(createBubble(text));
            messageDiv.appendChild(createTimeStamp());
            chatBody.appendChild(messageDiv);
        }

        function appendBotMessage(text, suggestions, products) {
            var messageDiv = document.createElement('div');
            messageDiv.className = 'ai-message bot';
            messageDiv.appendChild(createBubble(text || ''));

            if (products && products.length > 0) {
                messageDiv.appendChild(createProductList(products));
            }

            if (suggestions && suggestions.length > 0) {
                var wrapper = document.createElement('div');
                wrapper.className = 'ai-suggestions-wrapper';
                suggestions.forEach(function (suggestion) {
                    var chip = document.createElement('button');
                    chip.type = 'button';
                    chip.className = 'ai-chip';
                    chip.textContent = suggestion;
                    wrapper.appendChild(chip);
                });
                messageDiv.appendChild(wrapper);
            }

            messageDiv.appendChild(createTimeStamp());
            chatBody.appendChild(messageDiv);
        }

        /**
         * Builds the recommendation cards. Every field comes from the server-built
         * product list; nothing here is derived from provider text.
         */
        function createProductList(products) {
            var list = document.createElement('div');
            list.className = 'ai-product-list';

            products.forEach(function (product) {
                if (!product || !product.detailsUrl) {
                    return;
                }

                var card = document.createElement('a');
                card.className = 'ai-product-card';
                card.href = product.detailsUrl;

                var image = document.createElement('img');
                image.className = 'ai-product-image';
                image.src = product.imageUrl || PLACEHOLDER_IMAGE;
                image.alt = product.name || '';
                image.loading = 'lazy';
                image.addEventListener('error', function () {
                    if (image.src !== PLACEHOLDER_IMAGE) {
                        image.src = PLACEHOLDER_IMAGE;
                    }
                });
                card.appendChild(image);

                var info = document.createElement('div');
                info.className = 'ai-product-info';

                var name = document.createElement('span');
                name.className = 'ai-product-name';
                name.textContent = product.name || '';
                info.appendChild(name);

                var price = document.createElement('span');
                price.className = 'ai-product-price';
                price.textContent = formatPrice(product.price);
                info.appendChild(price);

                if (product.reason) {
                    var reason = document.createElement('span');
                    reason.className = 'ai-product-reason';
                    reason.textContent = product.reason;
                    info.appendChild(reason);
                }

                card.appendChild(info);
                list.appendChild(card);
            });

            return list;
        }

        function formatPrice(value) {
            var amount = typeof value === 'number' ? value : parseFloat(value);
            if (isNaN(amount)) {
                return '';
            }
            try {
                return amount.toLocaleString('tr-TR', {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }) + ' ₺';
            } catch (formatError) {
                return amount.toFixed(2) + ' ₺';
            }
        }

        function createBubble(text) {
            var bubble = document.createElement('div');
            bubble.className = 'ai-message-bubble';
            // textContent, never innerHTML: provider output can never become markup.
            bubble.textContent = text;
            return bubble;
        }

        function createTimeStamp() {
            var time = document.createElement('div');
            time.className = 'ai-message-time';
            time.textContent = new Date().toLocaleTimeString([], {
                hour: '2-digit',
                minute: '2-digit'
            });
            return time;
        }

        function showTypingIndicator() {
            var typingDiv = document.createElement('div');
            typingDiv.className = 'ai-message bot typing';

            var indicator = document.createElement('div');
            indicator.className = 'ai-typing-indicator';
            for (var i = 0; i < 3; i++) {
                var dot = document.createElement('div');
                dot.className = 'ai-typing-dot';
                indicator.appendChild(dot);
            }

            typingDiv.appendChild(indicator);
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
            root.classList.toggle('ai-widget-loading', pending);
            if (!pending && widgetBox.classList.contains('active')) {
                inputField.focus();
            }
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
            var tokenInput = root.querySelector('input[name="__RequestVerificationToken"]');
            return tokenInput ? tokenInput.value : '';
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
