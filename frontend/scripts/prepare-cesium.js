#!/usr/bin/env node
import { createRequire } from 'module';
import { fileURLToPath } from 'url';
import path from 'path';
import { cp, mkdir, rm, stat } from 'fs/promises';

const require = createRequire(import.meta.url);
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

async function copyCesiumRuntime() {
  const cesiumPackagePath = require.resolve('cesium/package.json');
  const cesiumPackageDir = path.dirname(cesiumPackagePath);
  const cesiumBuildDir = path.join(cesiumPackageDir, 'Build', 'Cesium');
  const targetDir = path.resolve(__dirname, '../public/cesium');

  try {
    const buildStats = await stat(cesiumBuildDir);
    if (!buildStats.isDirectory()) {
      throw new Error('Expected Cesium Build directory to be a folder.');
    }
  } catch (error) {
    throw new Error(
      'Unable to locate Cesium build assets. Make sure the "cesium" package is installed.'
    );
  }

  await rm(targetDir, { recursive: true, force: true });
  await mkdir(targetDir, { recursive: true });
  await cp(cesiumBuildDir, targetDir, { recursive: true });

  const relative = path.relative(path.resolve(__dirname, '..'), targetDir) || '.';
  console.log(`Cesium runtime copied to ${relative}`);
}

copyCesiumRuntime().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
