import { spawn } from 'node:child_process';
const child = spawn(
  process.execPath,
  [
    'node_modules/@angular/cli/bin/ng.js',
    'serve',
    '--host',
    '127.0.0.1',
    '--port',
    process.env.PORT || '4200',
    '--proxy-config',
    'proxy.config.cjs',
  ],
  { stdio: 'inherit', env: process.env },
);
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
child.on('exit', (code) => process.exit(code ?? 1));
