"use strict";

// Remove the code/state from the visible URL before notifying the opener. Never send tokens or query values.
history.replaceState(null, "", location.pathname);
if (window.opener) {
  window.opener.postMessage({ type: "agentconnector:atlassian", success: document.body.dataset.success === "true" }, location.origin);
  window.close();
}
