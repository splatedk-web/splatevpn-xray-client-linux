(function(){
 'use strict';
 const $=id=>document.getElementById(id);
 const section=document.createElement('section');section.className='card settings-section';section.style.minWidth='0';
 section.innerHTML=`<div class="settings-sec-header"><h3>Внешний HTTP-прокси</h3></div>
 <p class="settings-help">Если ваша сеть требует внешний прокси: SplateVPN → HTTP-прокси → VPN-сервер. Адрес и порт укажите из настроек своей сети. Для TUN права запрашиваются через polkit.</p>
 <div class="setting-item"><label for="setUpstreamProxyEnabled">Подключаться через внешний прокси</label><label class="toggle-switch"><input id="setUpstreamProxyEnabled" class="desktop-setting" type="checkbox"><span class="slider"></span></label></div>
 <div class="setting-item"><label for="setUpstreamProxyHost">Адрес без http://</label><input id="setUpstreamProxyHost" class="tactical-select desktop-setting" placeholder="proxy.example.com" maxlength="253" autocomplete="off"></div>
 <div class="setting-item"><label for="setUpstreamProxyPort">Порт</label><input id="setUpstreamProxyPort" class="tactical-select desktop-setting" type="number" min="1" max="65535" value="3128"></div>
 <div class="artifact-actions"><button type="button" class="btn-secondary" id="checkUpstreamProxy">Проверить CONNECT</button></div>
 <p class="settings-help">Введите адрес и порт HTTP-прокси своей сети. Поддерживается прокси без пароля; PAC-скрипты не применяются. При включении используется глобальный маршрут через VPN. UDP-транспорт VPN через HTTP CONNECT не поддерживается.</p>`;
 $('setRoutingMode').closest('.settings-section').after(section);
 function sync(){const on=$('setUpstreamProxyEnabled').checked;const route=$('setRoutingMode');route.disabled=on;if(on){route.value='global';const ai=$('setAiMode');if(ai&&ai.value!=='dns_vpn')ai.value='vpn';}}
 $('setUpstreamProxyEnabled').addEventListener('change',sync);
 const collect=window.SplateExtras.collectSettings;
 window.SplateExtras.collectSettings=()=>{sync();return {...collect(),upstreamProxyEnabled:$('setUpstreamProxyEnabled').checked,upstreamProxyHost:$('setUpstreamProxyHost').value.trim(),upstreamProxyPort:Number($('setUpstreamProxyPort').value)};};
 $('checkUpstreamProxy').onclick=()=>{if(window.SplateFlushSettings?.()===false)return;window.chrome?.webview?.postMessage({action:'checkUpstreamProxy'});};
 addEventListener('splateMessage',e=>{const {type,payload:p}=e.detail;if(type==='extendedSettings'){$('setUpstreamProxyEnabled').checked=!!p.UpstreamProxyEnabled;$('setUpstreamProxyHost').value=p.UpstreamProxyHost||'';$('setUpstreamProxyPort').value=p.UpstreamProxyPort||3128;sync();}if(type==='operation'&&p.operation==='networkTools')section.querySelectorAll('button').forEach(b=>b.disabled=p.busy);});
})();
