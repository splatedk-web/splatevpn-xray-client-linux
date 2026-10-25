(function () {
  'use strict';
  window.SplateBalancer = {
    mount(schema) {
      const $ = id => document.getElementById(id);
      const modes = [
        ['balanced', 'Автоматический · по умолчанию', 'Выбирает сервер при подключении, заменяет недоступный и ищет более быстрый при ухудшении связи.'],
        ['failover', 'Только при сбое', 'Сохраняет рабочий сервер. Переключается, только когда несколько проверок подряд подтвердили сбой.'],
        ['initial_only', 'Только при подключении', 'Один раз выбирает сервер при подключении. Во время сеанса не переключает, даже если сервер перестал отвечать.']
      ];
      // Match Android BalancerSettingsCard.kt, including units and defaults.
      const fields = [
        ['balancerHealthSeconds', 'setBalancerHealth', 'Интервал проверки связи', [2, 5, 10, 30], 2, v => `${v} с`, 'monitor'],
        ['balancerFailureConfirmations', 'setBalancerFailures', 'Неудачных проверок до смены', [2, 3, 5], 2, String, 'monitor'],
        ['balancerOptimizationMinutes', 'setBalancerOptimization', 'Плановый поиск лучшего сервера', [5, 15, 30, 60], 5, v => `${v} мин`, 'optimize'],
        ['balancerCooldownSeconds', 'setBalancerCooldown', 'Пауза между улучшениями', [60, 180, 600, 1800], 60, v => `${v / 60} мин`, 'optimize'],
        ['balancerImprovementPercent', 'setBalancerImprovement', 'Минимальный выигрыш по задержке', [30, 40, 50, 80], 30, v => `${v}%`, 'optimize']
      ];
      const section = document.createElement('section');
      section.id = 'balancerSettings';
      section.className = 'card settings-section balancer-card';
      section.innerHTML = `<div class="settings-sec-header"><h3>Настройки балансировщика</h3></div>
        <p id="balancerModeSummary" class="settings-help"></p>
        <p id="balancerDisabledHint" class="settings-help balancer-disabled-hint">Параметры применятся, когда включён автовыбор сервера.</p>
        <select id="setBalancerMode" class="desktop-setting" aria-label="Режим балансировщика" hidden>${modes.map(([value, title]) => `<option value="${value}">${title}</option>`).join('')}</select>
        <fieldset class="balancer-modes"><legend>Режим балансировщика</legend>${modes.map(([value, title, hint]) => `<label class="balancer-mode"><input type="radio" name="balancerModeChoice" value="${value}"><span><strong>${title}</strong><span>${hint}</span></span></label>`).join('')}</fieldset>
        <div class="balancer-options"></div>
        <button id="resetBalancer" class="link-btn balancer-reset" type="button">Вернуть настройки по умолчанию</button>`;
      $('setEnableTun').closest('.settings-section').after(section);
      schema.balancerMode = ['setBalancerMode', 'value'];
      for (const [key, id, label, options, initial, format, group] of fields) {
        const row = document.createElement('div'); row.className = 'setting-item'; row.dataset.balancerGroup = group;
        const title = document.createElement('label'); title.htmlFor = id; title.textContent = label;
        const select = document.createElement('select'); select.id = id; select.className = 'tactical-select desktop-setting';
        for (const value of options) select.add(new Option(format(value), String(value)));
        select.value = String(initial); row.append(title, select); section.querySelector('.balancer-options').append(row);
        schema[key] = [id, 'value'];
      }
      function render() {
        const mode = $('setBalancerMode').value;
        section.querySelectorAll('[name="balancerModeChoice"]').forEach(radio => { radio.checked = radio.value === mode; });
        $('balancerModeSummary').textContent = modes.find(m => m[0] === mode)?.[1] || '';
        document.querySelector('.auto-select-desc').textContent = modes.find(m => m[0] === mode)?.[1] || '';
        $('balancerDisabledHint').hidden = $('setAutoFallback').checked;
        section.querySelectorAll('[data-balancer-group]').forEach(row => {
          row.hidden = row.dataset.balancerGroup === 'monitor' ? mode === 'initial_only' : mode !== 'balanced';
        });
      }
      section.querySelectorAll('[name="balancerModeChoice"]').forEach(radio => radio.addEventListener('change', () => {
        if (!radio.checked) return;
        $('setBalancerMode').value = radio.value; render();
        $('setBalancerMode').dispatchEvent(new Event('change', { bubbles: true }));
      }));
      $('setBalancerMode').addEventListener('change', render);
      $('setAutoFallback').addEventListener('change', render);
      $('resetBalancer').onclick = () => {
        $('setBalancerMode').value = 'balanced';
        fields.forEach(([, id, , , initial]) => { $(id).value = String(initial); });
        render(); $('setBalancerMode').dispatchEvent(new Event('change', { bubbles: true }));
      };
      addEventListener('splateMessage', event => {
        if (event.detail.type === 'extendedSettings') {
          // Preserve earlier custom values rather than silently clearing them.
          fields.forEach(([key, id, , , , format]) => {
            const value = event.detail.payload[key[0].toUpperCase() + key.slice(1)];
            if (value != null && ![...$(id).options].some(o => o.value === String(value))) $(id).add(new Option(format(value), String(value)));
          });
        }
        if (['settings', 'extendedSettings'].includes(event.detail.type)) queueMicrotask(render);
      });
      const shortcut = document.createElement('button'); shortcut.id = 'btnBalancerSettings'; shortcut.type = 'button';
      shortcut.className = 'link-btn balancer-shortcut'; shortcut.textContent = 'Настроить балансировщик';
      $('toggleAutoFallback').closest('.auto-select-card').after(shortcut);
      shortcut.onclick = () => {
        $('navSettings').click();
        $('settingsCategories').querySelector('[data-category="balancer"]').click();
        section.scrollIntoView({ block: 'start' });
      };
      render();
    }
  };
})();
