const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

function handleReconnectStateChanged(event) {
    if (event.detail.state === "show") {
        reconnectModal.showModal();
    } else if (event.detail.state === "hide") {
        reconnectModal.close();
    } else if (event.detail.state === "failed") {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } else if (event.detail.state === "rejected") {
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        // Blazor.reconnect returns true on success, false when the server is reachable but rejects
        // the circuit, and throws when the server is not reachable.
        const successful = await Blazor.reconnect();
        if (!successful) {
            // The server is reachable but the circuit is gone, so try to resume it and reload only
            // if that fails.
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                location.reload();
            } else {
                reconnectModal.close();
            }
        }
    } catch (err) {
        // The server is not reachable, so retry when the tab becomes visible.
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            location.reload();
        }
    } catch {
        if (reconnectModal.classList.contains("components-reconnect-paused")) {
            reconnectModal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
        } else if (reconnectModal.classList.contains("components-pause")) {
            reconnectModal.classList.replace("components-pause", "components-resume-failed");
        } else {
            reconnectModal.classList.add("components-reconnect-resume-failed");
        }
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
