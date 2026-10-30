const target =
  process.env.services__categorization_api__http__0 ||
  process.env.API_BASE_URL ||
  'http://localhost:5075';
module.exports = { '/api/**': { target, secure: false, changeOrigin: true } };
