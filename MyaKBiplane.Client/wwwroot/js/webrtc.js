/*
 * Прямое соединение между браузерами через WebRTC DataChannel.
 * Сервер сигналинга не нужен: SDP и ICE-кандидаты игроки передают кодами вручную.
 */
(() => {
    "use strict";

    const iceServers = [
        { urls: "stun:stun.l.google.com:19302" },
        { urls: "stun:stun1.l.google.com:19302" }
    ];

    let connection = null;
    let channel = null;
    let dotNetRef = null;
    let isHost = false;
    let closed = true;
    let gatheredCandidates = [];

    function notify(method, value) {
        if (!dotNetRef) {
            return;
        }

        try {
            const result = value === undefined
                ? dotNetRef.invokeMethodAsync(method)
                : dotNetRef.invokeMethodAsync(method, value);
            result.catch(() => { });
        } catch (error) {
            // Компонент мог быть выгружен — соединение всё равно закрываем.
        }
    }

    function setupChannel(dataChannel) {
        channel = dataChannel;

        dataChannel.onopen = () => {
            if (!closed) {
                notify("OnWebRtcConnected");
            }
        };

        dataChannel.onmessage = (event) => {
            if (!closed && typeof event.data === "string") {
                notify("OnWebRtcMessage", event.data);
            }
        };

        dataChannel.onclose = () => {
            if (!closed) {
                notify("OnWebRtcDisconnected", "Прямое соединение закрыто");
            }
        };

        dataChannel.onerror = () => {
            if (!closed) {
                notify("OnWebRtcDisconnected", "Ошибка прямого соединения");
            }
        };
    }

    function waitForIce(pc, timeoutMs = 12000) {
        if (pc.iceGatheringState === "complete") {
            return Promise.resolve();
        }

        return new Promise((resolve) => {
            let finished = false;

            const finish = () => {
                if (finished) {
                    return;
                }

                finished = true;
                pc.removeEventListener("icegatheringstatechange", check);
                clearTimeout(timer);
                resolve();
            };

            const check = () => {
                if (pc.iceGatheringState === "complete") {
                    finish();
                }
            };

            const timer = setTimeout(finish, timeoutMs);
            pc.addEventListener("icegatheringstatechange", check);
        });
    }

    function start(host, ref) {
        close();

        isHost = host;
        dotNetRef = ref;
        closed = false;
        gatheredCandidates = [];

        if (typeof RTCPeerConnection === "undefined") {
            notify("OnWebRtcDisconnected", "Этот браузер не поддерживает прямое соединение");
            return;
        }

        connection = new RTCPeerConnection({ iceServers });

        connection.onicecandidate = (event) => {
            if (closed || !event.candidate) {
                return;
            }

            gatheredCandidates.push({
                candidate: event.candidate.candidate,
                sdpMid: event.candidate.sdpMid,
                sdpMLineIndex: event.candidate.sdpMLineIndex
            });
        };

        connection.onconnectionstatechange = () => {
            if (closed || !connection) {
                return;
            }

            if (connection.connectionState === "failed") {
                notify("OnWebRtcDisconnected", "Не удалось пробиться к сопернику через сеть");
            }
        };

        if (isHost) {
            setupChannel(connection.createDataChannel("game", { ordered: true }));
        } else {
            connection.ondatachannel = (event) => setupChannel(event.channel);
        }
    }

    function parseRtc(code) {
        const bundle = typeof code === "string" ? JSON.parse(code) : code;
        const rtc = typeof bundle.rtc === "string"
            ? JSON.parse(bundle.rtc)
            : (bundle.rtc || bundle);

        if (!rtc || !rtc.sdp) {
            throw new Error("В коде нет SDP");
        }

        return { bundle, rtc };
    }

    async function addCandidates(candidates) {
        for (const candidate of candidates || []) {
            try {
                await connection.addIceCandidate(candidate);
            } catch (error) {
                // Ненужные кандидаты можно пропустить.
            }
        }
    }

    async function createOfferCode() {
        if (closed || !isHost || !connection) {
            throw new Error("Соединение не готово");
        }

        try {
            const offer = await connection.createOffer();
            await connection.setLocalDescription(offer);
            await waitForIce(connection);

            return JSON.stringify({
                type: "offer",
                sdp: connection.localDescription.sdp,
                candidates: gatheredCandidates
            });
        } catch (error) {
            console.error("MyaK WebRTC: createOfferCode", error);
            notify("OnWebRtcDisconnected", "Не удалось создать код приглашения");
            throw error;
        }
    }

    async function createAnswerCode(code) {
        if (closed || isHost || !connection) {
            throw new Error("Соединение не готово");
        }

        try {
            const { rtc } = parseRtc(code);
            await connection.setRemoteDescription({ type: "offer", sdp: rtc.sdp });
            await addCandidates(rtc.candidates);

            const answer = await connection.createAnswer();
            await connection.setLocalDescription(answer);
            await waitForIce(connection);

            return JSON.stringify({
                type: "answer",
                sdp: connection.localDescription.sdp,
                candidates: gatheredCandidates
            });
        } catch (error) {
            console.error("MyaK WebRTC: createAnswerCode", error);
            notify("OnWebRtcDisconnected", "Не удалось создать код ответа");
            throw error;
        }
    }

    async function acceptAnswerCode(code) {
        if (closed || !isHost || !connection) {
            throw new Error("Соединение не готово");
        }

        try {
            const { rtc } = parseRtc(code);
            await connection.setRemoteDescription({ type: "answer", sdp: rtc.sdp });
            await addCandidates(rtc.candidates);
        } catch (error) {
            console.error("MyaK WebRTC: acceptAnswerCode", error);
            notify("OnWebRtcDisconnected", "Не удалось принять код ответа");
            throw error;
        }
    }

    function send(text) {
        if (closed || !channel || channel.readyState !== "open") {
            return false;
        }

        try {
            channel.send(text);
            return true;
        } catch (error) {
            return false;
        }
    }

    function close() {
        closed = true;

        if (channel) {
            try { channel.close(); } catch (error) { }
            channel = null;
        }

        if (connection) {
            try { connection.close(); } catch (error) { }
            connection = null;
        }

        gatheredCandidates = [];
        dotNetRef = null;
    }

    function debugState() {
        return {
            supported: typeof RTCPeerConnection !== "undefined",
            isHost,
            closed,
            connection: connection ? connection.connectionState : null,
            gathering: connection ? connection.iceGatheringState : null,
            channel: channel ? channel.readyState : null,
            localDescription: connection && connection.localDescription ? connection.localDescription.type : null,
            remoteDescription: connection && connection.remoteDescription ? connection.remoteDescription.type : null,
            localCandidates: gatheredCandidates.length,
            remoteCandidates: connection && connection.remoteDescription
                ? (connection.remoteDescription.sdp.match(/a=candidate/g) || []).length
                : 0
        };
    }

    window.myakWebrtc = {
        start,
        createOfferCode,
        createAnswerCode,
        acceptAnswerCode,
        send,
        close,
        debugState
    };
})();
