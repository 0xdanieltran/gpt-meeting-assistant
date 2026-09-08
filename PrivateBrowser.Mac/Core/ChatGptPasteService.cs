using System.Text.Json;

namespace PrivateBrowser.Mac
{
    public static class ChatGptPasteService
    {
        public const string ScreenshotAnalysisPrompt =
            "Please analyze the attached screenshot. If it contains a coding problem, written question, exam prompt, or other on-screen task, provide a correct and complete solution. If it shows code, identify any issues and include a corrected implementation. Present the answer clearly and precisely.";

        public static string BuildInsertAndSendScript(
            string text,
            bool send = true,
            bool replaceExisting = true
        )
        {
            string encodedText =
                JsonSerializer.Serialize(
                    text
                );

            string shouldSendJs =
                send ? "true" : "false";

            string replaceExistingJs =
                replaceExisting ? "true" : "false";

            return $$"""
                (async () => {
                    const text = {{encodedText}};
                    const shouldSend = {{shouldSendJs}};
                    const replaceExisting = {{replaceExistingJs}};
                    const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
                    const isVisible = (el) => {
                        if (!el) return false;
                        const style = window.getComputedStyle(el);
                        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') {
                            return false;
                        }
                        const rect = el.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0;
                    };
                    const normalize = (value) => (value || '').replace(/\s+/g, ' ').trim();
                    const composerHasText = (el, expected) => {
                        const current = normalize(el.innerText || el.value || '');
                        const target = normalize(expected);
                        if (!current || !target) return false;
                        const sample = target.slice(0, Math.min(48, target.length));
                        return current.includes(sample);
                    };
                    const findComposer = () => {
                        const prompt = document.querySelector('#prompt-textarea');
                        if (prompt) {
                            if (prompt.isContentEditable || prompt instanceof HTMLTextAreaElement) {
                                if (isVisible(prompt)) return prompt;
                            }
                            const nested = prompt.querySelector('[contenteditable="true"], textarea');
                            if (isVisible(nested)) return nested;
                        }
                        const selectors = [
                            '[data-testid="prompt-textarea"]',
                            'div.ProseMirror[contenteditable="true"]',
                            'div[contenteditable="true"][id*="prompt"]',
                            'form textarea',
                            'textarea',
                            'div[contenteditable="true"]'
                        ];
                        for (const selector of selectors) {
                            const visible = Array.from(document.querySelectorAll(selector)).find(isVisible);
                            if (visible) return visible;
                        }
                        return null;
                    };
                    const insertIntoTextarea = (el, value) => {
                        const prototype = el instanceof HTMLTextAreaElement
                            ? HTMLTextAreaElement.prototype
                            : HTMLInputElement.prototype;
                        const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
                        if (descriptor && descriptor.set) descriptor.set.call(el, value);
                        else el.value = value;
                        el.dispatchEvent(new Event('input', { bubbles: true }));
                        el.dispatchEvent(new Event('change', { bubbles: true }));
                    };
                    const selectAll = (el) => {
                        if (el instanceof HTMLTextAreaElement || el instanceof HTMLInputElement) {
                            el.select();
                            return;
                        }
                        const selection = window.getSelection();
                        const range = document.createRange();
                        range.selectNodeContents(el);
                        selection.removeAllRanges();
                        selection.addRange(range);
                    };
                    const placeCaretAtEnd = (el) => {
                        try {
                            const selection = window.getSelection();
                            const range = document.createRange();
                            range.selectNodeContents(el);
                            range.collapse(false);
                            selection.removeAllRanges();
                            selection.addRange(range);
                        } catch { }
                    };
                    const insertIntoComposer = async (el, value) => {
                        if (composerHasText(el, value)) return true;
                        try { window.focus(); el.focus(); el.click(); } catch { }
                        if (el instanceof HTMLTextAreaElement || el instanceof HTMLInputElement) {
                            insertIntoTextarea(
                                el,
                                replaceExisting ? value : ((el.value || '') + value)
                            );
                            await wait(50);
                            return composerHasText(el, value);
                        }
                        if (replaceExisting) {
                            selectAll(el);
                        } else {
                            placeCaretAtEnd(el);
                        }
                        try { document.execCommand('insertText', false, value); } catch { }
                        await wait(80);
                        if (composerHasText(el, value)) return true;
                        if (replaceExisting) {
                            selectAll(el);
                        }
                        try {
                            const dataTransfer = new DataTransfer();
                            dataTransfer.setData('text/plain', value);
                            el.dispatchEvent(new ClipboardEvent('paste', {
                                clipboardData: dataTransfer,
                                bubbles: true,
                                cancelable: true
                            }));
                        } catch { }
                        await wait(80);
                        return composerHasText(el, value);
                    };
                    const isUsableSendButton = (button) => {
                        if (!button || !isVisible(button)) return false;
                        const testId = (button.getAttribute('data-testid') || '').toLowerCase();
                        const label = (button.getAttribute('aria-label') || '').toLowerCase();
                        if (testId.includes('stop') || label.includes('stop')) return false;
                        if (button.disabled || button.getAttribute('aria-disabled') === 'true') return false;
                        return true;
                    };
                    const findSendButton = () => {
                        const selectors = [
                            '#composer-submit-button',
                            'button[data-testid="send-button"]',
                            'button[data-testid="composer-send-button"]',
                            'button[aria-label="Send prompt"]',
                            'button[aria-label="Send message"]',
                            'button[aria-label="Send"]'
                        ];
                        for (const selector of selectors) {
                            const button = document.querySelector(selector);
                            if (isUsableSendButton(button)) return button;
                        }
                        const form = document.querySelector('form');
                        if (form) {
                            const submit = form.querySelector('button[type="submit"]');
                            if (isUsableSendButton(submit)) return submit;
                        }
                        return null;
                    };
                    try {
                        let input = null;
                        for (let i = 0; i < 10; i++) {
                            input = findComposer();
                            if (input && input.getAttribute('contenteditable') !== 'false') break;
                            await wait(100);
                        }
                        if (!input) {
                            return JSON.stringify({ success: false, reason: 'input-not-found' });
                        }
                        for (let i = 0; i < 4; i++) {
                            if (composerHasText(input, text)) break;
                            await insertIntoComposer(input, text);
                            await wait(80);
                        }
                        if (!composerHasText(input, text)) {
                            return JSON.stringify({ success: false, reason: 'insert-failed' });
                        }
                        if (!shouldSend) {
                            return JSON.stringify({ success: true, method: 'draft' });
                        }
                        for (let i = 0; i < 20; i++) {
                            const sendButton = findSendButton();
                            if (sendButton) {
                                sendButton.click();
                                return JSON.stringify({ success: true, method: 'button' });
                            }
                            await wait(80);
                        }
                        input.focus();
                        const options = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true };
                        input.dispatchEvent(new KeyboardEvent('keydown', options));
                        input.dispatchEvent(new KeyboardEvent('keyup', options));
                        return JSON.stringify({ success: true, method: 'enter' });
                    } catch (error) {
                        return JSON.stringify({ success: false, reason: String(error) });
                    }
                })()
                """;
        }

        public static string BuildFocusComposerScript()
        {
            return """
                (() => {
                    const prompt = document.querySelector('#prompt-textarea, [data-testid="prompt-textarea"], div.ProseMirror[contenteditable="true"]');
                    if (!prompt) {
                        return false;
                    }
                    try { window.focus(); prompt.focus(); prompt.click(); } catch { }
                    return true;
                })()
                """;
        }

        public static string BuildAttachPngScript(
            string pngBase64
        )
        {
            string encoded =
                JsonSerializer.Serialize(
                    pngBase64
                );

            return $$"""
                (async () => {
                    const encoded = {{encoded}};
                    const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
                    const binary = atob(encoded);
                    const bytes = new Uint8Array(binary.length);
                    for (let i = 0; i < binary.length; i++) {
                        bytes[i] = binary.charCodeAt(i);
                    }
                    const file = new File([bytes], 'screenshot.png', { type: 'image/png' });
                    const isVisible = (el) => {
                        if (!el) return false;
                        const style = window.getComputedStyle(el);
                        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') {
                            return false;
                        }
                        const rect = el.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0;
                    };
                    const findComposer = () => {
                        const prompt = document.querySelector('#prompt-textarea');
                        if (prompt) {
                            if (prompt.isContentEditable || prompt instanceof HTMLTextAreaElement) {
                                if (isVisible(prompt)) return prompt;
                            }
                            const nested = prompt.querySelector('[contenteditable="true"], textarea');
                            if (isVisible(nested)) return nested;
                        }
                        const selectors = [
                            '[data-testid="prompt-textarea"]',
                            'div.ProseMirror[contenteditable="true"]',
                            'div[contenteditable="true"]'
                        ];
                        for (const selector of selectors) {
                            const visible = Array.from(document.querySelectorAll(selector)).find(isVisible);
                            if (visible) return visible;
                        }
                        return null;
                    };
                    const hasAttachment = (input) => {
                        if (input && input.files && input.files.length > 0) return true;
                        return !!(
                            document.querySelector('[data-testid="file-preview"]') ||
                            document.querySelector('[data-testid="composer-attachment"]') ||
                            document.querySelector('button[aria-label*="Remove file" i]') ||
                            document.querySelector('button[aria-label*="Remove" i][aria-label*="png" i]') ||
                            document.querySelector('img[src^="blob:"]')
                        );
                    };
                    const assignToInput = () => {
                        document.querySelectorAll('[data-pb-upload]').forEach((el) => {
                            el.removeAttribute('data-pb-upload');
                        });
                        const inputs = Array.from(document.querySelectorAll('input[type="file"]'));
                        const target = inputs.find((el) => {
                            const accept = (el.accept || '').toLowerCase();
                            return accept.includes('image') ||
                                accept.includes('png') ||
                                accept.includes('*') ||
                                accept === '';
                        }) || inputs[0];
                        if (!target) {
                            return false;
                        }
                        try {
                            const dataTransfer = new DataTransfer();
                            dataTransfer.items.add(file);
                            target.files = dataTransfer.files;
                            target.setAttribute('data-pb-upload', '1');
                            target.dispatchEvent(new Event('input', { bubbles: true }));
                            target.dispatchEvent(new Event('change', { bubbles: true }));
                            return target.files && target.files.length > 0;
                        } catch {
                            return false;
                        }
                    };
                    const clickAttach = () => {
                        const attach = document.querySelector(
                            'button[aria-label*="Attach" i], button[aria-label*="Add files" i], button[aria-label*="Upload" i], button[data-testid="composer-plus-btn"]'
                        );
                        if (attach) {
                            attach.click();
                            return true;
                        }
                        return false;
                    };
                    const pasteOrDrop = (el) => {
                        try {
                            const dataTransfer = new DataTransfer();
                            dataTransfer.items.add(file);
                            el.focus();
                            el.dispatchEvent(new ClipboardEvent('paste', {
                                clipboardData: dataTransfer,
                                bubbles: true,
                                cancelable: true
                            }));
                            el.dispatchEvent(new DragEvent('drop', {
                                dataTransfer,
                                bubbles: true,
                                cancelable: true
                            }));
                            return true;
                        } catch {
                            return false;
                        }
                    };
                    try {
                        if (assignToInput()) {
                            await wait(250);
                            return JSON.stringify({ success: true, method: 'file-input' });
                        }
                        if (clickAttach()) {
                            await wait(220);
                            if (assignToInput()) {
                                await wait(250);
                                return JSON.stringify({ success: true, method: 'file-input-after-click' });
                            }
                        }
                        const composer = findComposer();
                        if (composer) {
                            pasteOrDrop(composer);
                            await wait(350);
                            if (hasAttachment()) {
                                return JSON.stringify({ success: true, method: 'composer-event' });
                            }
                        }
                        return JSON.stringify({ success: false, reason: 'attach-failed' });
                    } catch (error) {
                        return JSON.stringify({ success: false, reason: String(error) });
                    }
                })()
                """;
        }
    }
}
