// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.
// Write your JavaScript code.


function copyToClipboard(elementId) {
    var element = document.getElementById(elementId);
    element.select();
    element.setSelectionRange(0, 99999); /*For mobile devices*/
    document.execCommand("copy");
    alert("Saved to clipboard");
}