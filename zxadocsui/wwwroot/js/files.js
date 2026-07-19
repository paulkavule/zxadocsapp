// Triggers a browser download of bytes fetched (with auth) by the app — a plain link to
// the API endpoint can't carry the Bearer token, so the client fetches then saves here.
window.zxFiles = {
  save: function (fileName, base64, contentType) {
    const a = document.createElement("a");
    a.href = "data:" + (contentType || "application/octet-stream") + ";base64," + base64;
    a.download = fileName || "download";
    document.body.appendChild(a);
    a.click();
    a.remove();
  },
};
