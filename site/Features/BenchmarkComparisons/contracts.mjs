export const IDS = Object.freeze({ benchmarks: 'benchmarks', scene: 'cluster-scene', motion: 'scene-motion' });

export const SELECTORS = Object.freeze({ command: '.command-box code', copy: '#copy-command',
  poster: '.cluster-poster', sceneStatus: '[data-scene-status]' });

export const CONFIG = Object.freeze({
  median: 'median',
  units: Object.freeze({ millisecondsPerSecond: 1000, bytesPerKiB: 1024, bytesPerMiB: 1048576, percent: 100 }),
  copyResetMs: 1800, revisionPattern: /^[a-f0-9]{40}$/,
  evidencePattern: /^https:\/\/github\.com\/managedcode\/KeyLoad\/actions\/runs\/[1-9]\d*$/,
});

export const TEXT = Object.freeze({ invalidReport: 'The selected measurement is invalid.',
  sourceCheckout: 'Source checkout', copied: 'Copied', copy: 'Copy' });
