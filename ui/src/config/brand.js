// Central app branding. Override at build time with VITE_APP_NAME / VITE_APP_SHORT_NAME
// (e.g. in ui/.env.production, ui/.env.local, or the deployment's environment) to
// white-label the app without touching source — every place that previously hardcoded
// "CRS Reporter" / "CRS" should import from here instead.
export const APP_NAME = import.meta.env.VITE_APP_NAME || 'CRS Reporter';
export const APP_SHORT_NAME = import.meta.env.VITE_APP_SHORT_NAME || 'CRS';
