(function () {
    "use strict";

    // Чистый сетевой RTT через отдельный WebSocket-echo сервера (/ws/ping).
    // Не использует SignalR и .NET<->JS-маршаллинг, поэтому показывает,
    // где именно теряется время: в сети или внутри клиента/хаба.
    function measure(url, count, timeoutMs) {
        count = Math.max(1, Math.min(20, count || 10));
        timeoutMs = timeoutMs || 3000;

        return new Promise((resolve) => {
            const samples = [];
            let index = 0;

            function finish(value) {
                samples.push(value);
                index += 1;
                window.setTimeout(next, 20);
            }

            function next() {
                if (index >= count) {
                    const valid = samples.filter((value) => value >= 0).slice().sort((a, b) => a - b);
                    const min = valid.length ? valid[0] : -1;
                    const max = valid.length ? valid[valid.length - 1] : -1;
                    const avg = valid.length
                        ? valid.reduce((sum, value) => sum + value, 0) / valid.length
                        : -1;

                    const result = {
                        min: Math.round(min * 10) / 10,
                        avg: Math.round(avg * 10) / 10,
                        max: Math.round(max * 10) / 10,
                        samples: samples.map((value) => Math.round(value * 10) / 10)
                    };

                    console.info("MyaK NetDiag", url, result);
                    resolve([result.min, result.avg, result.max]);
                    return;
                }

                let socket;
                let done = false;
                let timer = null;

                const complete = (value) => {
                    if (done) {
                        return;
                    }

                    done = true;

                    if (timer !== null) {
                        window.clearTimeout(timer);
                    }
                    finish(value);

                    try {
                        socket.close();
                    } catch (error) {
                        // Сокет мог уже закрыться.
                    }
                };

                timer = window.setTimeout(() => complete(-1), timeoutMs);

                try {
                    socket = new WebSocket(url);
                } catch (error) {
                    complete(-1);
                    return;
                }

                const sentAt = performance.now();

                socket.onopen = () => {
                    try {
                        socket.send(String(sentAt));
                    } catch (error) {
                        complete(-1);
                    }
                };
                socket.onmessage = (event) => {
                    const echoed = Number(event.data);
                    complete(Number.isFinite(echoed) ? performance.now() - echoed : -1);
                };
                socket.onerror = () => complete(-1);
                socket.onclose = () => complete(-1);
            }

            next();
        });
    }

    window.myakNetDiag = { measure: measure };
})();
