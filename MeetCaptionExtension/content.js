(() => {
    console.log("[MeetCaption] Extension loaded");

    // ============================================================
    // CONFIG
    // ============================================================

    const CAPTION_REGION_SELECTOR =
        '[role="region"][aria-label="Captions"]';

    const CAPTION_BLOCK_SELECTOR =
        ".nMcdL";

    const SPEAKER_SELECTOR =
        ".NWpY1d";

    const CAPTION_TEXT_SELECTOR =
        ".ygicle";


    /*
     * Maximum frequency at which a changing Meet caption
     * is forwarded to PrivateBrowser.
     *
     * 120ms gives responsive updates without sending
     * every single DOM mutation.
     */
    const CAPTION_UPDATE_INTERVAL =
        120;


    /*
     * Extension storage is secondary.
     *
     * We do NOT wait for chrome.storage before forwarding
     * captions to PrivateBrowser.
     */
    const STORAGE_SAVE_DELAY =
        1000;


    // ============================================================
    // STATE
    // ============================================================

    const captionState = {
        latest: null,
        history: []
    };


    let captionRegion =
        null;

    let captionObserver =
        null;

    let pageObserver =
        null;


    // ============================================================
    // CAPTION BLOCK IDS
    // ============================================================

    const captionBlockIds =
        new WeakMap();

    let nextCaptionBlockId =
        1;


    // ============================================================
    // LOW-LATENCY CAPTION UPDATE STATE
    // ============================================================

    /*
     * block -> timeout
     *
     * Used when another update happens before the
     * 120ms interval has elapsed.
     */
    const captionUpdateTimers =
        new Map();


    /*
     * block -> timestamp of last send
     */
    const lastCaptionSendTimes =
        new Map();


    /*
     * block -> last text sent
     *
     * Prevents sending exactly the same text twice.
     */
    const processedValues =
        new Map();


    // ============================================================
    // STORAGE TIMER
    // ============================================================

    let storageSaveTimer =
        null;


    // ============================================================
    // MEETING STATE
    // ============================================================

    function getMeetingKey() {
        return window.location.pathname
            .replace(/^\/+|\/+$/g, "")
            .toLowerCase();
    }


    let currentMeetingKey =
        getMeetingKey();


    let lastUrl =
        window.location.href;


    // ============================================================
    // HELPERS
    // ============================================================

    function normalizeText(value) {
        return (value || "")
            .replace(/\s+/g, " ")
            .trim();
    }


    function getErrorMessage(error) {
        if (!error)
            return "Unknown error";

        if (typeof error === "string")
            return error;

        return error.message ??
            String(error);
    }


    function isContextInvalidatedError(error) {
        return getErrorMessage(error)
            .toLowerCase()
            .includes(
                "extension context invalidated"
            );
    }


    function isExtensionContextValid() {
        try {
            return !!(
                chrome &&
                chrome.runtime &&
                chrome.runtime.id
            );
        }
        catch {
            return false;
        }
    }


    // ============================================================
    // SEGMENT ID
    // ============================================================

    function getCaptionBlockId(block) {
        let id =
            captionBlockIds.get(
                block
            );


        if (!id) {
            id =
                `${currentMeetingKey}-segment-${nextCaptionBlockId++}`;


            captionBlockIds.set(
                block,
                id
            );
        }


        return id;
    }


    // ============================================================
    // READ GOOGLE MEET CAPTION BLOCK
    // ============================================================

    function readCaptionBlock(block) {
        if (!(block instanceof HTMLElement))
            return null;


        const speakerElement =
            block.querySelector(
                SPEAKER_SELECTOR
            );


        const textElement =
            block.querySelector(
                CAPTION_TEXT_SELECTOR
            );


        if (!textElement)
            return null;


        const text =
            normalizeText(
                textElement.innerText ||
                textElement.textContent
            );


        if (!text)
            return null;


        let speaker =
            null;


        if (speakerElement) {
            speaker =
                normalizeText(
                    speakerElement.innerText ||
                    speakerElement.textContent
                );


            if (!speaker) {
                speaker =
                    null;
            }
        }


        return {
            speaker,
            text
        };
    }


    // ============================================================
    // UPDATE EXTENSION MEMORY
    // ============================================================

    function updateLocalHistory(item) {
        captionState.latest =
            item;


        /*
         * Same segmentId means Meet is extending
         * an existing caption block.
         *
         * Replace it rather than append another
         * cumulative version.
         */
        const existingIndex =
            captionState.history.findIndex(
                existing =>
                    existing.segmentId ===
                    item.segmentId
            );


        if (existingIndex >= 0) {
            captionState.history[
                existingIndex
            ] = item;
        }
        else {
            captionState.history.push(
                item
            );
        }


        // Keep development history bounded.
        if (
            captionState.history.length >
            1000
        ) {
            captionState.history =
                captionState.history.slice(
                    -1000
                );
        }
    }


    // ============================================================
    // STORAGE
    // ============================================================

    async function saveStorageNow() {
        if (!isExtensionContextValid())
            return;


        try {
            await chrome.storage.local.set({
                meetCaptionMeetingKey:
                    currentMeetingKey,

                meetCaptionLatest:
                    captionState.latest,

                meetCaptionHistory:
                    captionState.history
            });
        }
        catch (error) {
            if (
                !isContextInvalidatedError(
                    error
                )
            ) {
                console.warn(
                    "[MeetCaption] Storage error:",
                    getErrorMessage(error)
                );
            }
        }
    }


    function scheduleStorageSave() {
        if (storageSaveTimer) {
            clearTimeout(
                storageSaveTimer
            );
        }


        storageSaveTimer =
            setTimeout(
                () => {
                    storageSaveTimer =
                        null;

                    void saveStorageNow();
                },
                STORAGE_SAVE_DELAY
            );
    }


    // ============================================================
    // SEND CAPTION
    // ============================================================

    async function storeCaption(
        caption,
        block
    ) {
        try {
            if (
                !caption?.text ||
                !block
            ) {
                return;
            }


            const text =
                normalizeText(
                    caption.text
                );


            if (!text)
                return;


            const item = {
                meetingKey:
                    currentMeetingKey,

                segmentId:
                    getCaptionBlockId(
                        block
                    ),

                speaker:
                    caption.speaker ??
                    null,

                text,

                timestamp:
                    new Date()
                        .toISOString()
            };


            // --------------------------------------------------------
            // MEMORY FIRST
            // --------------------------------------------------------

            updateLocalHistory(
                item
            );


            console.log(
                "[MeetCaption] CAPTURED:",
                {
                    segmentId:
                        item.segmentId,

                    speaker:
                        item.speaker,

                    text:
                        item.text
                }
            );


            // --------------------------------------------------------
            // PRIVATEBROWSER FIRST
            //
            // Do not wait for chrome.storage before this.
            // --------------------------------------------------------

            if (
                isExtensionContextValid()
            ) {
                try {
                    const response =
                        await chrome.runtime
                            .sendMessage({
                                type:
                                    "MEET_CAPTION",

                                caption:
                                    item
                            });


                    if (!response?.ok) {
                        console.warn(
                            "[MeetCaption] PrivateBrowser unavailable:",
                            response?.error ??
                            "Unknown bridge error"
                        );
                    }
                }
                catch (error) {
                    if (
                        !isContextInvalidatedError(
                            error
                        )
                    ) {
                        console.warn(
                            "[MeetCaption] Bridge error:",
                            getErrorMessage(
                                error
                            )
                        );
                    }
                }
            }


            // --------------------------------------------------------
            // STORAGE LATER
            // --------------------------------------------------------

            scheduleStorageSave();
        }
        catch (error) {
            console.warn(
                "[MeetCaption] storeCaption error:",
                getErrorMessage(error)
            );
        }
    }


    // ============================================================
    // ACTUALLY SEND CURRENT VALUE OF A BLOCK
    // ============================================================

    function sendCaptionBlock(block) {
        if (!block)
            return;


        const caption =
            readCaptionBlock(
                block
            );


        if (
            !caption ||
            !caption.text
        ) {
            return;
        }


        const normalizedText =
            normalizeText(
                caption.text
            );


        if (!normalizedText)
            return;


        const previousValue =
            processedValues.get(
                block
            );


        // Exact same text already sent.
        if (
            previousValue ===
            normalizedText
        ) {
            return;
        }


        /*
         * Mark before async bridge call so a second mutation
         * doesn't immediately send the exact same value.
         */
        processedValues.set(
            block,
            normalizedText
        );


        lastCaptionSendTimes.set(
            block,
            Date.now()
        );


        void storeCaption(
            {
                speaker:
                    caption.speaker ??
                    null,

                text:
                    normalizedText
            },
            block
        ).catch(
            error => {
                if (
                    !isContextInvalidatedError(
                        error
                    )
                ) {
                    console.warn(
                        "[MeetCaption] Store failed:",
                        getErrorMessage(
                            error
                        )
                    );
                }
            }
        );
    }


    // ============================================================
    // LOW-LATENCY THROTTLE
    // ============================================================

    function scheduleCaption(block) {
        if (!block)
            return;


        const caption =
            readCaptionBlock(
                block
            );


        if (
            !caption ||
            !caption.text
        ) {
            return;
        }


        const normalizedText =
            normalizeText(
                caption.text
            );


        if (!normalizedText)
            return;


        const previousValue =
            processedValues.get(
                block
            );


        if (
            previousValue ===
            normalizedText
        ) {
            return;
        }


        const now =
            Date.now();


        const lastSendTime =
            lastCaptionSendTimes.get(
                block
            ) ?? 0;


        const elapsed =
            now -
            lastSendTime;


        // --------------------------------------------------------
        // Enough time has elapsed:
        // send immediately.
        // --------------------------------------------------------

        if (
            elapsed >=
            CAPTION_UPDATE_INTERVAL
        ) {
            const oldTimer =
                captionUpdateTimers.get(
                    block
                );


            if (oldTimer) {
                clearTimeout(
                    oldTimer
                );

                captionUpdateTimers.delete(
                    block
                );
            }


            sendCaptionBlock(
                block
            );

            return;
        }


        // --------------------------------------------------------
        // Too soon:
        // schedule exactly one send for the newest value.
        // --------------------------------------------------------

        const existingTimer =
            captionUpdateTimers.get(
                block
            );


        if (existingTimer) {
            /*
             * Do not keep resetting the entire interval.
             *
             * The existing timer is already scheduled for
             * the earliest permitted send time.
             */
            return;
        }


        const remaining =
            Math.max(
                0,
                CAPTION_UPDATE_INTERVAL -
                elapsed
            );


        const timer =
            setTimeout(
                () => {
                    captionUpdateTimers.delete(
                        block
                    );


                    sendCaptionBlock(
                        block
                    );
                },
                remaining
            );


        captionUpdateTimers.set(
            block,
            timer
        );
    }


    // ============================================================
    // FIND CAPTION BLOCK FROM MUTATION
    // ============================================================

    function findCaptionBlock(node) {
        let element =
            null;


        if (node instanceof HTMLElement) {
            element =
                node;
        }
        else if (
            node?.nodeType ===
            Node.TEXT_NODE &&
            node.parentElement
        ) {
            element =
                node.parentElement;
        }


        if (!element)
            return null;


        /*
         * Mutation occurred somewhere inside an
         * existing caption block.
         */
        const closest =
            element.closest(
                CAPTION_BLOCK_SELECTOR
            );


        if (
            closest &&
            captionRegion?.contains(
                closest
            )
        ) {
            return closest;
        }


        /*
         * Mutation may itself be a new caption block.
         */
        if (
            element.matches?.(
                CAPTION_BLOCK_SELECTOR
            )
            &&
            captionRegion?.contains(
                element
            )
        ) {
            return element;
        }


        return null;
    }


    // ============================================================
    // SCAN EXISTING BLOCKS
    // ============================================================

    function scanCaptionBlocks() {
        if (!captionRegion)
            return;


        const blocks =
            captionRegion.querySelectorAll(
                CAPTION_BLOCK_SELECTOR
            );


        for (const block of blocks) {
            scheduleCaption(
                block
            );
        }
    }


    // ============================================================
    // CAPTION MUTATION OBSERVER
    // ============================================================

    function startCaptionObserver(region) {
        if (!region)
            return;


        if (
            captionRegion === region &&
            captionObserver
        ) {
            return;
        }


        if (captionObserver) {
            captionObserver.disconnect();
        }


        captionRegion =
            region;


        console.log(
            "[MeetCaption] Caption region found"
        );


        captionObserver =
            new MutationObserver(
                mutations => {
                    /*
                     * Multiple DOM mutations can refer to
                     * the same caption block.
                     *
                     * Deduplicate affected blocks before
                     * scheduling them.
                     */
                    const affectedBlocks =
                        new Set();


                    for (
                        const mutation
                        of mutations
                    ) {
                        // ----------------------------------------
                        // TEXT CHANGED
                        // ----------------------------------------

                        if (
                            mutation.type ===
                            "characterData"
                        ) {
                            const block =
                                findCaptionBlock(
                                    mutation.target
                                );


                            if (block) {
                                affectedBlocks.add(
                                    block
                                );
                            }


                            continue;
                        }


                        // ----------------------------------------
                        // DOM CHILDREN CHANGED
                        // ----------------------------------------

                        if (
                            mutation.type ===
                            "childList"
                        ) {
                            const parentBlock =
                                findCaptionBlock(
                                    mutation.target
                                );


                            if (parentBlock) {
                                affectedBlocks.add(
                                    parentBlock
                                );
                            }


                            for (
                                const node
                                of mutation.addedNodes
                            ) {
                                const block =
                                    findCaptionBlock(
                                        node
                                    );


                                if (block) {
                                    affectedBlocks.add(
                                        block
                                    );
                                }


                                if (
                                    node instanceof
                                    HTMLElement
                                ) {
                                    const nested =
                                        node.querySelectorAll(
                                            CAPTION_BLOCK_SELECTOR
                                        );


                                    for (
                                        const childBlock
                                        of nested
                                    ) {
                                        if (
                                            captionRegion?.contains(
                                                childBlock
                                            )
                                        ) {
                                            affectedBlocks.add(
                                                childBlock
                                            );
                                        }
                                    }
                                }
                            }
                        }
                    }


                    for (
                        const block
                        of affectedBlocks
                    ) {
                        scheduleCaption(
                            block
                        );
                    }
                }
            );


        captionObserver.observe(
            region,
            {
                subtree:
                    true,

                childList:
                    true,

                characterData:
                    true
            }
        );


        /*
         * Capture anything already visible.
         */
        scanCaptionBlocks();
    }


    // ============================================================
    // FIND CAPTION REGION
    // ============================================================

    function findCaptionRegion() {
        return document.querySelector(
            CAPTION_REGION_SELECTOR
        );
    }


    function tryAttachCaptionObserver() {
        const region =
            findCaptionRegion();


        if (region) {
            startCaptionObserver(
                region
            );

            return true;
        }


        return false;
    }


    // ============================================================
    // PAGE OBSERVER
    //
    // Captions region does not exist until captions are enabled.
    // ============================================================

    function startPageObserver() {
        if (pageObserver)
            return;


        pageObserver =
            new MutationObserver(
                () => {
                    if (
                        !captionRegion ||
                        !document.contains(
                            captionRegion
                        )
                    ) {
                        captionRegion =
                            null;


                        tryAttachCaptionObserver();
                    }
                }
            );


        pageObserver.observe(
            document.documentElement,
            {
                subtree:
                    true,

                childList:
                    true
            }
        );
    }


    // ============================================================
    // RESTORE EXTENSION STATE
    // ============================================================

    async function restoreHistory() {
        try {
            if (
                !isExtensionContextValid()
            ) {
                return;
            }


            const meetingKey =
                getMeetingKey();


            const result =
                await chrome.storage.local.get([
                    "meetCaptionMeetingKey",
                    "meetCaptionLatest",
                    "meetCaptionHistory"
                ]);


            const savedMeetingKey =
                result.meetCaptionMeetingKey ??
                null;


            // --------------------------------------------------------
            // DIFFERENT MEETING
            // --------------------------------------------------------

            if (
                savedMeetingKey &&
                savedMeetingKey !==
                meetingKey
            ) {
                console.log(
                    "[MeetCaption] New meeting detected:",
                    savedMeetingKey,
                    "→",
                    meetingKey
                );


                captionState.latest =
                    null;

                captionState.history =
                    [];


                await chrome.storage.local.set({
                    meetCaptionMeetingKey:
                        meetingKey,

                    meetCaptionLatest:
                        null,

                    meetCaptionHistory:
                        []
                });


                return;
            }


            // --------------------------------------------------------
            // FIRST TIME
            // --------------------------------------------------------

            if (!savedMeetingKey) {
                await chrome.storage.local.set({
                    meetCaptionMeetingKey:
                        meetingKey
                });
            }


            // --------------------------------------------------------
            // RESTORE
            // --------------------------------------------------------

            if (
                result.meetCaptionLatest
            ) {
                captionState.latest =
                    result.meetCaptionLatest;
            }


            if (
                Array.isArray(
                    result.meetCaptionHistory
                )
            ) {
                captionState.history =
                    result.meetCaptionHistory;
            }


            console.log(
                "[MeetCaption] Meeting:",
                meetingKey
            );


            console.log(
                "[MeetCaption] Existing history:",
                captionState.history.length
            );
        }
        catch (error) {
            if (
                !isContextInvalidatedError(
                    error
                )
            ) {
                console.warn(
                    "[MeetCaption] Restore error:",
                    getErrorMessage(
                        error
                    )
                );
            }
        }
    }


    // ============================================================
    // CLEAR CAPTION UPDATE TIMERS
    // ============================================================

    function clearCaptionTimers() {
        for (
            const timer
            of captionUpdateTimers.values()
        ) {
            clearTimeout(
                timer
            );
        }


        captionUpdateTimers.clear();

        lastCaptionSendTimes.clear();

        processedValues.clear();


        if (storageSaveTimer) {
            clearTimeout(
                storageSaveTimer
            );

            storageSaveTimer =
                null;
        }
    }


    // ============================================================
    // MEETING URL CHANGE
    // ============================================================

    function handleMeetingUrlChange() {
        const newMeetingKey =
            getMeetingKey();


        if (
            !newMeetingKey ||
            newMeetingKey ===
            currentMeetingKey
        ) {
            return;
        }


        console.log(
            "[MeetCaption] Meeting changed:",
            currentMeetingKey,
            "→",
            newMeetingKey
        );


        currentMeetingKey =
            newMeetingKey;


        nextCaptionBlockId =
            1;


        captionState.latest =
            null;

        captionState.history =
            [];


        clearCaptionTimers();


        /*
         * Existing caption region usually gets replaced by Meet
         * during navigation. Force re-detection if necessary.
         */
        if (
            captionRegion &&
            !document.contains(
                captionRegion
            )
        ) {
            captionRegion =
                null;
        }


        if (
            isExtensionContextValid()
        ) {
            void chrome.storage.local.set({
                meetCaptionMeetingKey:
                    currentMeetingKey,

                meetCaptionLatest:
                    null,

                meetCaptionHistory:
                    []
            }).catch(
                error => {
                    if (
                        !isContextInvalidatedError(
                            error
                        )
                    ) {
                        console.warn(
                            "[MeetCaption] Meeting reset storage error:",
                            getErrorMessage(
                                error
                            )
                        );
                    }
                }
            );
        }


        tryAttachCaptionObserver();
    }


    // ============================================================
    // DEBUG
    // ============================================================

    window.__meetCaptionDebug = {
        getLatest() {
            return captionState.latest;
        },


        getHistory() {
            return captionState.history;
        },


        getCaptionRegion() {
            return captionRegion;
        },


        getMeetingKey() {
            return currentMeetingKey;
        },


        clear() {
            captionState.latest =
                null;

            captionState.history =
                [];


            clearCaptionTimers();


            if (
                isExtensionContextValid()
            ) {
                void chrome.storage.local.remove([
                    "meetCaptionLatest",
                    "meetCaptionHistory"
                ]);
            }


            console.log(
                "[MeetCaption] History cleared"
            );
        }
    };


    // ============================================================
    // START
    // ============================================================

    void restoreHistory();


    /*
     * Captions might already be enabled.
     */
    tryAttachCaptionObserver();


    /*
     * Otherwise monitor Meet until the caption region appears.
     */
    startPageObserver();


    /*
     * Meet is a SPA, so URL changes do not always reload
     * the content script.
     */
    setInterval(
        () => {
            if (
                window.location.href !==
                lastUrl
            ) {
                lastUrl =
                    window.location.href;


                handleMeetingUrlChange();
            }
        },
        500
    );
})();