"use strict";

const tokenInput = document.getElementById("admin-token");
const nameInput = document.getElementById("connection-name");
const status = document.getElementById("status");
const connections = document.getElementById("connections");
const signInButtons = [...document.querySelectorAll("[data-provider]")];
let pending = null;

function finish(message, closePopup = false) {
  if (pending) {
    clearInterval(pending.timer);
    if (closePopup) pending.popup.close();
    pending = null;
  }
  signInButtons.forEach(button => { button.disabled = false; });
  status.textContent = message;
}

signInButtons.forEach(button => button.addEventListener("click", async () => {
  const token = tokenInput.value;
  const connectionName = nameInput.value.trim();
  if (!token || !connectionName) {
    status.textContent = "Enter your connector admin token and a connection name.";
    return;
  }
  // Open synchronously inside the click handler so popup blockers can recognize the user gesture.
  const popup = window.open("about:blank", "_blank", "popup,width=620,height=760");
  if (!popup) {
    status.textContent = "Allow popups for this site, then try again.";
    return;
  }
  signInButtons.forEach(item => { item.disabled = true; });
  status.textContent = "Preparing Atlassian sign-in…";
  const attempt = { popup, expiresAt: Date.now() + 10 * 60 * 1000 };
  pending = attempt;
  attempt.timer = setInterval(() => {
    if (popup.closed) finish("The sign-in window closed. Use Check connections to confirm the result.");
    else if (Date.now() > attempt.expiresAt) finish("Sign-in expired. Please try again.", true);
  }, 500);
  try {
    const response = await fetch(`/connector/api/oauth/${button.dataset.provider}/authorize`, {
      method: "POST", headers: { "Authorization": `Bearer ${token}`, "Content-Type": "application/json" },
      body: JSON.stringify({ connectionName }), cache: "no-store", credentials: "omit"
    });
    if (!response.ok) throw new Error("authorization failed");
    const result = await response.json();
    const authorization = new URL(result.data.authorizationUri);
    if (authorization.origin !== "https://auth.atlassian.com" || authorization.pathname !== "/authorize")
      throw new Error("unexpected authorization URL");
    if (pending !== attempt) return;
    popup.location.replace(authorization.href);
    status.textContent = "Complete sign-in and consent in the Atlassian popup.";
  } catch {
    if (pending === attempt) finish("Could not start sign-in. Check the admin token and server configuration.", true);
  }
}));

window.addEventListener("message", event => {
  if (!pending || event.origin !== location.origin || event.source !== pending.popup ||
      event.data?.type !== "agentconnector:atlassian" || typeof event.data.success !== "boolean") return;
  finish(event.data.success
    ? "Authorization completed. Use Check connections to see the stored connection."
    : "Authorization was denied, failed, or expired. Please try again.", true);
});

document.getElementById("check-connections").addEventListener("click", async () => {
  connections.textContent = "";
  if (!tokenInput.value) {
    status.textContent = "Enter your connector admin token to check connections.";
    return;
  }
  try {
    const response = await fetch("/connector/api/connections", {
      headers: { "Authorization": `Bearer ${tokenInput.value}` }, cache: "no-store", credentials: "omit"
    });
    if (!response.ok) throw new Error("connection lookup failed");
    const result = await response.json();
    const atlassian = result.data.filter(item => item.providerId === "jira" || item.providerId === "confluence");
    connections.textContent = JSON.stringify(atlassian, null, 2);
    status.textContent = "Stored connections loaded. Run a read action to verify access to the configured site.";
  } catch {
    status.textContent = "Could not load connections. Check your connector admin token.";
  }
});

window.addEventListener("pagehide", () => {
  tokenInput.value = "";
  if (pending) {
    clearInterval(pending.timer);
    pending.popup.close();
  }
});
