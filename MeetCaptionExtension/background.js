const PRIVATE_BROWSER_API =
    "http://127.0.0.1:17832";

chrome.runtime.onMessage.addListener(
    (message, sender, sendResponse) => {

        if (
            !message ||
            message.type !== "MEET_CAPTION"
        ) {
            return;
        }

        sendCaption(message.caption)
            .then(() => {
                sendResponse({
                    ok: true
                });
            })
            .catch(error => {
                console.error(
                    "[MeetCaptionBridge] Send failed:",
                    error
                );

                sendResponse({
                    ok: false,
                    error:
                        error?.message ??
                        String(error)
                });
            });

        return true;
    }
);

async function sendCaption(caption) {
    if (!caption?.text)
        return;

    const response =
        await fetch(
            `${PRIVATE_BROWSER_API}/captions`,
            {
                method: "POST",

                headers: {
                    "Content-Type":
                        "application/json"
                },

                body: JSON.stringify({
                    meetingKey:
                        caption.meetingKey ?? null,

                    segmentId:
                        caption.segmentId ?? null,

                    text:
                        caption.text,

                    speaker:
                        caption.speaker ?? null,

                    timestamp:
                        caption.timestamp ??
                        new Date().toISOString()
                })
            }
        );

    if (!response.ok) {
        throw new Error(
            `PrivateBrowser returned ${response.status}`
        );
    }

    console.log(
        "[MeetCaptionBridge] SENT:",
        caption.text
    );
}