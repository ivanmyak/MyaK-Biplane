/*
 * Прямое соединение между браузерами через WebRTC DataChannel.
 * Сервер используется только для обмена SDP/ICE — внутри матча он не участвует.
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
    let pendingCandidates = [];
    let candidateCount = 0;

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

    function sendSignal(payload) {
        console.debug("MyaK WebRTC signal out", payload.type);
        notify("OnWebRtcSignal", JSON.stringify(payload));
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

    function start(host, ref) {
        close();

        isHost = host;
        dotNetRef = ref;
        closed = false;
        pendingCandidates = [];
        candidateCount = 0;

        if (typeof RTCPeerConnection === "undefined") {
            notify("OnWebRtcDisconnected", "Этот браузер не поддерживает прямое соединение");
            return;
        }

        connection = new RTCPeerConnection({ iceServers });

        connection.onicecandidate = (event) => {
            if (closed) {
                return;
            }

            if (!event.candidate) {
                if (candidateCount === 0) {
                    notify("OnWebRtcDisconnected",
                        "Браузер не нашёл сетевой адрес для прямого соединения. Проверьте настройки сети.");
                }
                return;
            }

            candidateCount++;
            const candidate = event.candidate;
            sendSignal({
                type: "candidate",
                candidate: {
                    candidate: candidate.candidate,
                    sdpMid: candidate.sdpMid,
                    sdpMLineIndex: candidate.sdpMLineIndex
                }
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

    async function createOffer() {
        if (closed || !isHost || !connection) {
            return;
        }

        try {
            const offer = await connection.createOffer();
            await connection.setLocalDescription(offer);
            sendSignal({ type: "description", sdp: connection.localDescription });
        } catch (error) {
            notify("OnWebRtcDisconnected", "Не удалось создать предложение соединения");
        }
    }

    async function handleSignal(json) {
        if (closed || !connection) {
            return;
        }

        let message;

        try {
            message = typeof json === "string" ? JSON.parse(json) : json;
        } catch (error) {
            return;
        }

        console.debug("MyaK WebRTC signal in", message.type);

        try {
            if (message.type === "ready") {
                // Гость готов принимать предложение — хост может создавать offer.
                notify("OnPeerReady");
                return;
            }

            if (message.type === "description" && message.sdp) {
                await connection.setRemoteDescription(message.sdp);

                for (const candidate of pendingCandidates) {
                    await connection.addIceCandidate(candidate);
                }
                pendingCandidates = [];

                if (message.sdp.type === "offer") {
                    const answer = await connection.createAnswer();
                    await connection.setLocalDescription(answer);
                    sendSignal({ type: "description", sdp: connection.localDescription });
                }
            } else if (message.type === "candidate" && message.candidate) {
                if (connection.remoteDescription) {
                    await connection.addIceCandidate(message.candidate);
                } else {
                    // Кандидат пришёл раньше SDP — дождёмся описания.
                    pendingCandidates.push(message.candidate);
                }
            }
        } catch (error) {
            notify("OnWebRtcDisconnected", "Ошибка при установке прямого соединения");
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

        pendingCandidates = [];
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
            localCandidates: connection && connection.localDescription
                ? (connection.localDescription.sdp.match(/a=candidate/g) || []).length
                : 0,
            remoteCandidates: connection && connection.remoteDescription
                ? (connection.remoteDescription.sdp.match(/a=candidate/g) || []).length
                : 0
        };
    }

    window.myakWebrtc = {
        start,
        createOffer,
        handleSignal,
        send,
        close,
        debugState
    };
})();
