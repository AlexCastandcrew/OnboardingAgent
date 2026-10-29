// Forwards /api to the backend and adds the bearer token on the way, so the browser never sees it.
// The token comes from the ONBOARDING_TOKEN environment variable of this process (see scripts/start-web.ps1).
const target = process.env.ONBOARDING_API_URL || 'http://localhost:5006';
const token = process.env.ONBOARDING_TOKEN;

module.exports = {
  '/api': {
    target,
    secure: false,
    changeOrigin: true,
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  },
};
