/**
 * Triggers a browser file download from a byte array.
 * @param {string} fileName - The suggested file name for the download.
 * @param {string} contentType - The MIME type of the file.
 * @param {Uint8Array} byteArray - The file content as a byte array.
 */
window.downloadFileFromBytes = function (fileName, contentType, byteArray) {
    const blob = new Blob([byteArray], { type: contentType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
};
