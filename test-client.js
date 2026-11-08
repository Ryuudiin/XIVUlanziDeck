// Teste rápido do servidor sem o Ulanzi: node test-client.js <token> [porta]
// Requer: npm install ws
const WebSocket = require('ws');
const [token, port = 37984] = process.argv.slice(2);
if (!token) { console.error('Uso: node test-client.js <token> [porta]'); process.exit(1); }

const ws = new WebSocket(`ws://127.0.0.1:${port}/?token=${token}`);
ws.on('open', () => {
  console.log('Conectado');
  ws.send(JSON.stringify({ id: '1', action: 'ping' }));
  ws.send(JSON.stringify({ id: '2', action: 'getPlayerInfo' }));
  ws.send(JSON.stringify({ id: '3', action: 'executeCommand', data: { command: '/e dançando pelo Ulanzi' } }));
});
ws.on('message', (m) => console.log('<-', m.toString()));
ws.on('close', (c) => console.log('Fechado', c));
ws.on('error', (e) => console.error('Erro:', e.message));
setTimeout(() => ws.close(), 3000);
