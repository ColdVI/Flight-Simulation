#!/usr/bin/env node
import compression from 'compression';
import express from 'express';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
const port = Number(process.env.PORT || 8080);
const publicDir = path.join(__dirname, 'public');
const cesiumDir = path.join(publicDir, 'cesium');

app.use(compression());

if (fs.existsSync(cesiumDir)) {
  app.use('/cesium', express.static(cesiumDir, { cacheControl: true, maxAge: '1y' }));
} else {
  console.warn('Cesium runtime assets were not found. Run "npm run prepare" first.');
}

app.use(express.static(__dirname, { extensions: ['html'] }));

app.get('*', (req, res, next) => {
  const entryFile = path.join(__dirname, 'index.html');
  res.sendFile(entryFile, (error) => {
    if (error) {
      next(error);
    }
  });
});

app.listen(port, () => {
  console.log(`Flight Simulation frontend available at http://localhost:${port}`);
});
