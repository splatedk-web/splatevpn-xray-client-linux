// Preserve the desktop IPC protocol on Photino/WebKitGTK.
(() => {
  const listeners = new Set();
  const received = new Set();
  let reported = false;
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    postMessage: message => window.external.sendMessage(JSON.stringify(message)),
    addEventListener: (name, listener) => { if (name === 'message') listeners.add(listener); },
    removeEventListener: (name, listener) => listeners.delete(listener)
  };
  window.external.receiveMessage(message => {
    let data; try { data = JSON.parse(message); } catch { return; }
    received.add(data.type);
    if(data.type === 'linuxWidget') document.body.classList.toggle('linux-widget',Boolean(data.payload));
    if(data.type === 'state') {
      const status=document.getElementById('linuxWidgetStatus');if(status)status.textContent=data.payload.statusText;
      const server=document.getElementById('linuxWidgetServer');if(server)server.textContent=data.payload.selectedServer?.displayName || 'Сервер не выбран';
    }
    if(data.type === 'traffic') {
      const speed=document.getElementById('linuxWidgetSpeed');if(speed)speed.textContent='↓ '+data.payload.downloadSpeed+'  ↑ '+data.payload.uploadSpeed;
    }
    for (const listener of listeners) listener({ data });
    if (!reported && ['state','settings','servers','subscriptions'].every(type => received.has(type))) {
      reported = true;
      setTimeout(() => window.external.sendMessage(JSON.stringify({ action: 'linuxSmoke', report: {
        readyState: document.readyState, title: document.title,
        received: [...received], themes: Object.keys(window.SplatePalettes || {}).length + 1,
        hasApp: !!document.querySelector('.app-shell'),
        hasConnectButton: !!document.getElementById('btnToggleConnection'),
        hasBalancerSettings: !!document.getElementById('balancerSettings'),
        balancerModes: document.querySelectorAll('[name="balancerModeChoice"]').length,
        windowsProxyImport: !!document.getElementById('importUpstreamProxy'),
        horizontalOverflow: document.documentElement.scrollWidth > innerWidth,
        width: innerWidth, height: innerHeight
      }})), 2000);
    }
  });
  document.addEventListener('DOMContentLoaded',()=> {
    const style=document.createElement('style');
    style.textContent='body.linux-widget .app-shell{display:none}#linuxWidget{display:none}body.linux-widget #linuxWidget{display:flex;flex-direction:column;gap:12px;padding:20px;min-width:0}body.linux-widget{min-width:0!important;overflow:hidden}';
    document.head.append(style);
    const widget=document.createElement('section');widget.id='linuxWidget';
    widget.innerHTML='<strong>SplateVPN</strong><div id="linuxWidgetStatus">Отключено</div><div id="linuxWidgetServer"></div><div id="linuxWidgetSpeed"></div><div><button class="btn-secondary" id="linuxWidgetToggle">Подключить / отключить</button> <button class="btn-secondary" id="linuxWidgetRestore">Открыть</button></div>';
    document.body.append(widget);
    document.getElementById('linuxWidgetToggle').onclick=()=>window.chrome.webview.postMessage({action:'toggleConnection'});
    document.getElementById('linuxWidgetRestore').onclick=()=>window.chrome.webview.postMessage({action:'windowAction',command:'restore'});
  });
  document.addEventListener('click', event => {
    const anchor = event.target.closest?.('a[href]');
    if (anchor && /^(https?:|file:|javascript:)/i.test(anchor.getAttribute('href'))) event.preventDefault();
  }, true);
})();
