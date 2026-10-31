export const IDS = Object.freeze({ benchmarks: 'benchmarks', scene: 'cluster-scene', menu: 'site-menu' });

export const SELECTORS = Object.freeze({ command: '.command-box code', copy: '#copy-command', results: '#bench-results',
  poster: '.cluster-poster', sceneStatus: '[data-scene-status]', menuLink: 'a[href]', menuOpen: ':popover-open' });

export const CONFIG = Object.freeze({
  copyResetMs: 1800, wideNavigationQuery: '(min-width: 761px)', revisionPattern: /^[a-f0-9]{40}$/,
});

export const TEXT = Object.freeze({ sourceCheckout: 'Source checkout', copied: 'Copied', copy: 'Copy' });
