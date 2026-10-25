const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

(async () => {
  const browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL || 'msedge' });
  try {
    const page = await browser.newPage({ viewport: { width: 1180, height: 900 }, reducedMotion: 'reduce' });
    const errors = []; page.on('pageerror', error => { errors.push(error.message); console.error('PAGE:', error.message); });
    await page.addInitScript(() => {
      window.__sent = [];
      window.external = { sendMessage: text => __sent.push(JSON.parse(text)), receiveMessage: fn => window.__host = fn };
    });
    await page.goto('file:///' + path.resolve('wwwroot/index.html').replaceAll('\\', '/'));
    const host = (type, payload) => page.evaluate(({ type, payload }) => __host(JSON.stringify({ type, payload,
      settingsRequestId: __sent.filter(m => m.action === 'saveSettings').at(-1)?.settingsRequestId })), { type, payload });
    await host('settings', { theme: 'dark', httpPort: 10809, socksPort: 10808, autoFallbackFastestServer: true });
    await host('extendedSettings', { BalancerMode: 'balanced', BalancerHealthSeconds: 2, BalancerFailureConfirmations: 2,
      BalancerOptimizationMinutes: 5, BalancerCooldownSeconds: 60, BalancerImprovementPercent: 30 });
    assert.deepEqual(errors, []);
    assert.notEqual(await page.locator('#tab-settings .settings-grid > .settings-section').first().getAttribute('id'), 'balancerSettings');
    assert.deepEqual(await page.locator('#settingsCategories button').evaluateAll(nodes => nodes.slice(0,4).map(n=>n.dataset.category)), ['all','appearance','network','balancer']);
    await page.locator('#btnBalancerSettings').click();
    assert.equal(await page.locator('#settingsCategories [data-category="balancer"]').getAttribute('aria-pressed'), 'true');
    assert(await page.locator('#balancerSettings').isVisible());
    assert.equal(await page.locator('.balancer-mode').count(), 3);
    for (const [id, values] of Object.entries({ setBalancerHealth: ['2', '5', '10', '30'], setBalancerFailures: ['2', '3', '5'],
      setBalancerOptimization: ['5', '15', '30', '60'], setBalancerCooldown: ['60', '180', '600', '1800'], setBalancerImprovement: ['30', '40', '50', '80'] })) {
      assert.deepEqual(await page.locator('#' + id + ' option').evaluateAll(options => options.map(o => o.value)), values);
    }
    await page.locator('[name="balancerModeChoice"][value="failover"]').check();
    assert(await page.locator('#setBalancerHealth').isVisible());
    assert(!(await page.locator('#setBalancerOptimization').isVisible()));
    await page.locator('#setBalancerHealth').selectOption('10');
    await page.locator('#setBalancerFailures').selectOption('5');
    await page.evaluate(() => SplateFlushSettings());
    let saved = await page.evaluate(() => __sent.filter(m => m.action === 'saveSettings').at(-1).settings);
    assert.equal(saved.balancerMode, 'failover'); assert.equal(Number(saved.balancerHealthSeconds), 10); assert.equal(Number(saved.balancerFailureConfirmations), 5);
    await page.locator('[name="balancerModeChoice"][value="initial_only"]').check();
    assert(!(await page.locator('#setBalancerHealth').isVisible()));
    await page.locator('#resetBalancer').click(); await page.evaluate(() => SplateFlushSettings());
    saved = await page.evaluate(() => __sent.filter(m => m.action === 'saveSettings').at(-1).settings);
    assert.equal(saved.balancerMode, 'balanced');
    assert.deepEqual([saved.balancerHealthSeconds, saved.balancerFailureConfirmations, saved.balancerOptimizationMinutes, saved.balancerCooldownSeconds, saved.balancerImprovementPercent].map(Number), [2, 2, 5, 60, 30]);
    await host('extendedSettings', { BalancerMode: 'balanced', BalancerHealthSeconds: 7, BalancerFailureConfirmations: 4,
      BalancerOptimizationMinutes: 15, BalancerCooldownSeconds: 120, BalancerImprovementPercent: 50 });
    assert.equal(await page.locator('#setBalancerHealth').inputValue(), '7');
    assert.equal(await page.locator('#setBalancerCooldown').inputValue(), '120');
    await page.locator('#resetBalancer').click();
    fs.mkdirSync('tests/artifacts/2.14.2', { recursive: true });
    await page.screenshot({ path: 'tests/artifacts/2.14.2/balancer.png', fullPage: true });
    await page.locator('#settingsCategories [data-category="advanced"]').click();
    assert.equal(await page.locator('#importUpstreamProxy').count(), 0);
    assert(await page.locator('#checkUpstreamProxy').isVisible());
    await page.screenshot({ path: 'tests/artifacts/2.14.2/linux-proxy.png', fullPage: true });
    assert(!/Windows|Wintun|WFP/.test(await page.locator('body').textContent()));
    await page.locator('#navHome').click();
    await host('settings', {theme:'dark',httpPort:10809,socksPort:10808,enableTunMode:true});
    await host('state', {isConnected:true,activeTunMode:false,statusText:'ПОДКЛЮЧЕНО',pendingConnectionSettings:true});
    assert.match(await page.locator('#tunModeStatus').textContent(), /TUN не применён/);
    assert.match(await page.locator('#microAdapter').textContent(), /Системный прокси/);
    await host('state', {isConnected:true,activeTunMode:true,statusText:'СВЯЗЬ НЕ ПОДТВЕРЖДЕНА'});
    assert.match(await page.locator('#tunModeStatus').textContent(), /TUN работает/);
    assert(!(await page.locator('#headerStatusIndicator').getAttribute('class')).split(' ').includes('connected'));
    await host('state', {isConnected:true,activeTunMode:true,statusText:'ПОДКЛЮЧЕНО'});
    assert((await page.locator('#headerStatusIndicator').getAttribute('class')).split(' ').includes('connected'));
    await host('state', {isConnected:false,activeTunMode:false,isConnecting:true,statusText:'ПОДКЛЮЧЕНИЕ...'});
    assert.match(await page.locator('#btnToggleConnectionText').textContent(), /Отменить/);
    await host('state', {isConnected:false,activeTunMode:false,isConnecting:false,statusText:'ОТКЛЮЧЕНО'});
    assert.match(await page.locator('#btnToggleConnectionText').textContent(), /Подключиться/);
    await page.locator('#navSettings').click();
    for (const width of [820, 1180]) {
      await page.setViewportSize({ width, height: 900 });
      await page.locator('#settingsCategories [data-category="balancer"]').click();
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
    }
    assert.deepEqual(errors, []);
    console.log('PASS Linux UI: Android balancer modes/options, conditional fields, reset, saved IPC, legacy values, discoverability, Linux-only settings, narrow layout');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
