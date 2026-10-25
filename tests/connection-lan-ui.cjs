const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.PLAYWRIGHT_CHANNEL||'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:1180,height:900}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{window.__sent=[];window.external={sendMessage:t=>__sent.push(JSON.parse(t)),receiveMessage:f=>window.__host=f};});
  await page.goto('file:///'+path.resolve('wwwroot/index.html').replaceAll('\\','/'));
  const host=(type,payload)=>page.evaluate(({type,payload})=>__host(JSON.stringify({type,payload,settingsRequestId:__sent.filter(m=>m.action==='saveSettings').at(-1)?.settingsRequestId})),{type,payload});
  await host('settings',{theme:'dark',httpPort:10809,socksPort:10808,enableTunMode:false});
  await host('extendedSettings',{BalancerMode:'balanced',BalancerHealthSeconds:2,BalancerFailureConfirmations:2,BalancerOptimizationMinutes:5,BalancerCooldownSeconds:60,BalancerImprovementPercent:30});
  await page.locator('#navSettings').click();
  assert((await page.evaluate(()=>__sent)).some(m=>m.action==='refreshLanInterfaces'));
  await host('lanInterfaces',[{name:'eth0',address:'192.168.1.2'},{name:'wlan0',address:'192.168.2.3'}]);
  assert.equal(await page.locator('#setLanBindAddress option').count(),3);
  await page.locator('#lanInterfaceChoices button').filter({hasText:'wlan0'}).click();
  assert.equal(await page.locator('#setLanBindAddress').inputValue(),'192.168.2.3');
  await page.evaluate(()=>SplateFlushSettings());
  assert.equal(await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings.lanBindAddress),'192.168.2.3');
  await page.locator('#refreshLanInterfaces').click();
  await host('lanInterfaces',[{name:'eth0',address:'192.168.1.2'}]);
  assert.equal(await page.locator('#setLanBindAddress').inputValue(),'192.168.2.3');
  assert.match(await page.locator('#setLanBindAddress option:checked').textContent(),/недоступен/);
  await host('lanInterfaces',[]);assert.match(await page.locator('#lanInterfaceStatus').textContent(),/Нет доступных/);
  await page.locator('#navHome').click();
  const steps=[{label:'Проверка портов',elapsedMs:0},{label:'Запуск ядра',elapsedMs:420},{label:'Ожидание polkit',elapsedMs:740}];
  await host('state',{isConnected:false,isConnecting:true,statusText:'ПОДКЛЮЧЕНИЕ...',connectionSteps:steps,connectionElapsedMs:1000});
  assert(await page.locator('#connectionProgress').isVisible());
  assert.equal(await page.locator('#connectionSteps li').count(),3);
  assert.match(await page.locator('#connectionSteps li.current').textContent(),/Ожидание polkit/);
  assert.match(await page.locator('#connectionElapsed').textContent(),/1.0 с/);
  await host('state',{isConnected:true,isConnecting:false,activeTunMode:true,statusText:'ПОДКЛЮЧЕНО',connectionSteps:[...steps,{label:'TUN готов',elapsedMs:1300}],connectionElapsedMs:1300});
  assert.equal(await page.locator('#connectionSteps li').count(),4);
  assert.equal(await page.locator('#connectionSteps li.current').count(),0);
  assert.deepEqual(errors,[]);
  console.log('PASS LAN UI: refresh, interface buttons, saved binding, missing-address preservation; connection stages, timing and completed history');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
