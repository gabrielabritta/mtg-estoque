// Botão da extensão: na página "Meus Pedidos" da LigaMagic abre todas as compras, espera os itens
// carregarem e copia no formato que o MTG Estoque entende (#ligamagic / qtd⇥nome⇥ed⇥foil⇥img).
chrome.action.onClicked.addListener(async tab => {
  const ok = /^https:\/\/www\.ligamagic\.com\.br\/.*view=mp\/compras/.test(tab.url || '');
  if (!tab.url?.startsWith('https://www.ligamagic.com.br/')) {
    chrome.tabs.create({ url: 'https://www.ligamagic.com.br/?view=mp/compras' });
    return;
  }
  chrome.scripting.executeScript({ target: { tabId: tab.id }, func: ok ? copyPurchases : notHere });
});

function notHere() {
  const d = document.createElement('div');
  d.textContent = 'Abra Minha Conta → Meus Pedidos e clique de novo.';
  d.style.cssText = 'position:fixed;right:20px;bottom:20px;z-index:99999;background:#1a1a1a;color:#fff;padding:12px 16px;border-radius:8px;font:14px sans-serif;box-shadow:0 4px 16px rgba(0,0,0,.3)';
  document.body.appendChild(d); setTimeout(() => d.remove(), 3500);
}

// Roda dentro da página (precisa ser autocontida).
async function copyPurchases() {
  const toast = (msg, ms = 4000) => {
    document.getElementById('mtg-toast')?.remove();
    const d = document.createElement('div'); d.id = 'mtg-toast'; d.textContent = msg;
    d.style.cssText = 'position:fixed;right:20px;bottom:20px;z-index:99999;background:#1a1a1a;color:#fff;padding:12px 16px;border-radius:8px;font:14px sans-serif;box-shadow:0 4px 16px rgba(0,0,0,.3)';
    document.body.appendChild(d); if (ms) setTimeout(() => d.remove(), ms);
  };

  // Só as compras que o usuário abriu ("Visualizar Itens"/"Itens" mostram #venda_<id>).
  const ids = [...document.querySelectorAll('[id^="venda_"]')]
    .filter(b => /^venda_\d+$/.test(b.id) && getComputedStyle(b).display !== 'none').map(b => b.id.slice(6));
  if (!ids.length) { toast('Abra uma compra em "Visualizar Itens" e clique de novo.'); return; }

  // espera os itens aparecerem ("N unid."); desiste em 20 s
  toast('Lendo ' + ids.length + ' compra(s)…', 0);
  const loaded = id => /\d+\s*unid\./.test(document.getElementById('venda_' + id)?.textContent || '');
  for (let t = 0; t < 80 && !ids.every(loaded); t++) await new Promise(r => setTimeout(r, 250));
  const missing = ids.filter(id => !loaded(id)).length;
  // guia aberta há muito tempo: o token do ajax expira e a Liga responde "precisa estar logado"
  if (ids.some(id => /estar logado/.test(document.getElementById('venda_' + id).textContent))) {
    toast('A página expirou — aperte F5 e clique de novo.'); return;
  }

  // o link às vezes traz entidade HTML no nome (L&oacute;rien)
  const decode = s => new DOMParser().parseFromString(s, 'text/html').documentElement.textContent;
  const rows = new Map();
  for (const a of ids.flatMap(id => [...document.getElementById('venda_' + id).querySelectorAll('a')])) {
    let u; try { u = new URL(a.href, location.href); } catch { continue; }
    if (!u.searchParams.has('card') || !u.searchParams.has('ed')) continue;
    let cell = a; while (cell && !cell.querySelector('img')) cell = cell.parentElement;      // célula com a imagem
    let row = cell; while (row && !/\d+\s*unid\./.test(row.textContent)) row = row.parentElement; // linha com a quantidade
    if (!row || rows.has(row)) continue;
    const t = row.textContent.replace(/\s+/g, ' ');
    const qty = (t.match(/(\d+)\s*unid\./) || [])[1] || '1';
    const img = [...row.querySelectorAll('img')].map(i => i.src).find(s => s.includes('/arquivos/in/')) || '';
    rows.set(row, [qty, decode(u.searchParams.get('card')), u.searchParams.get('ed'), /\bFoil\b/.test(t) ? 'foil' : '', img].join('\t'));
  }
  if (!rows.size) { toast('Nenhum item encontrado nas compras.'); return; }

  // sem gesto do usuário o navigator.clipboard pode falhar; execCommand funciona com a permissão clipboardWrite
  const ta = document.createElement('textarea');
  ta.value = '#ligamagic\n' + [...rows.values()].join('\n');
  document.body.appendChild(ta); ta.select(); document.execCommand('copy'); ta.remove();

  const cards = [...rows.values()].reduce((s, l) => s + +l.split('\t')[0], 0);
  let msg = cards + ' cartas copiadas (' + ids.length + (ids.length > 1 ? ' compras)' : ' compra)') + ' — cole no MTG Estoque em "Adicionar lista"';
  if (missing) msg += ' · ' + missing + ' não carregaram';
  toast(msg, 7000);
}
