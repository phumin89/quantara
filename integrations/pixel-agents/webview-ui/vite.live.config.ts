import { defineConfig, mergeConfig } from 'vite';
import baseConfig from './vite.config.js';

export default mergeConfig(baseConfig, defineConfig({
  base: './',
  build: { outDir: '../dist/live-office', emptyOutDir: true, rollupOptions: { input: 'live.html' } },
}));
