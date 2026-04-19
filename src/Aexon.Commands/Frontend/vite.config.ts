import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'path';

declare const process: {
  env: Record<string, string | undefined>;
};

const apiProxyTarget = process.env.AEVATAR_API_URL || '';

if (apiProxyTarget) {
  console.log(`[vite] API proxy: /api → ${apiProxyTarget}`);
}

export default defineConfig({
  plugins: [react()],
  base: '/',
  server: {
    ...(apiProxyTarget
      ? {
          proxy: {
            '/api': {
              target: apiProxyTarget,
              changeOrigin: true,
              secure: false,
              cookieDomainRewrite: { '*': '' },
              configure: (proxy: any) => {
                proxy.on('proxyReq', (proxyReq: any) => {
                  proxyReq.removeHeader('origin');
                  proxyReq.removeHeader('referer');
                });
                proxy.on('proxyRes', (proxyRes: any) => {
                  const setCookie = proxyRes.headers['set-cookie'];
                  if (setCookie) {
                    proxyRes.headers['set-cookie'] = setCookie.map((cookie: string) =>
                      cookie
                        .replace(/;\s*Secure/gi, '')
                        .replace(/;\s*SameSite=\w+/gi, '; SameSite=Lax'),
                    );
                  }
                });
              },
            },
          },
        }
      : {}),
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    cssCodeSplit: false,
    rollupOptions: {
      input: {
        chat: resolve(__dirname, 'index.html'),
        workbench: resolve(__dirname, 'workbench.html'),
      },
      output: {
        // JS lands per-entry; assets dump into staging and are relocated
        // by scripts/relocate-html.js.
        entryFileNames: 'aevatar-[name]/app.js',
        chunkFileNames: 'aevatar-[name]/[name]-[hash].js',
        assetFileNames: 'staging/[name]-[hash][extname]',
      },
    },
  },
});
