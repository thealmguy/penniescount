export function show(dialog) {
    // Blazor handles Escape so an in-flight save cannot be dismissed.
    dialog.oncancel = event => event.preventDefault();
    dialog.showModal();
}

export function close(dialog) {
    dialog.close();
}
