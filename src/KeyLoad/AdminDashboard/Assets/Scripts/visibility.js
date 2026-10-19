let observer;
export function observe(reference) {
    observer = () => reference.invokeMethodAsync('VisibilityChanged', !document.hidden);
    document.addEventListener('visibilitychange', observer);
    return !document.hidden;
}
export function stop() {
    document.removeEventListener('visibilitychange', observer);
    observer = undefined;
}
