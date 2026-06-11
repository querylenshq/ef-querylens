export function extractCompileMsFromHover(text: string): number | undefined {
    const timing = text.match(/SQL generation time\s+(\d+)\s*ms/i);
    if (!timing) {
        return undefined;
    }

    const compileMs = Number.parseInt(timing[1], 10);
    return Number.isFinite(compileMs) ? compileMs : undefined;
}

export function isLikelyCachedHover(roundTripMs: number, compileMs: number): boolean {
    return roundTripMs < 500 || roundTripMs < compileMs * 0.2;
}

export function formatHoverReadyMessage(
    fileName: string,
    line: number,
    character: number,
    roundTripMs: number,
    hoverText: string,
): string {
    const compileMs = extractCompileMsFromHover(hoverText);
    const roundedTrip = Math.round(roundTripMs);

    if (compileMs !== undefined) {
        const cached = isLikelyCachedHover(roundTripMs, compileMs);
        return cached
            ? `[EFQueryLens] hover-ready file=${fileName} line=${line} char=${character} roundTripMs=${roundedTrip} cached=true compileMs=${compileMs}`
            : `[EFQueryLens] hover-ready file=${fileName} line=${line} char=${character} roundTripMs=${roundedTrip} compileMs=${compileMs}`;
    }

    return `[EFQueryLens] hover-ready file=${fileName} line=${line} char=${character} roundTripMs=${roundedTrip}`;
}

export function formatHoverQueuedMessage(
    fileName: string,
    line: number,
    character: number,
    roundTripMs: number,
): string {
    return `[EFQueryLens] hover-queued file=${fileName} line=${line} char=${character} roundTripMs=${Math.round(roundTripMs)}`;
}
