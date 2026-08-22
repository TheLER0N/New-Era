namespace MainApp;

// Бридж WebView2. Ответ привязан к запросу через id (__LERON_REQID__):
// стрим захватывает свой id в момент перехвата fetch и шлёт его в aiResponse,
// поэтому хвост старого ответа не может закрыть новый запрос.
// EXPECT взводится ТОЛЬКО после завершения старого стрима (в SendScript).
// ВАЖНО: ExecuteScriptAsync не ждёт Promise — результаты скрипты шлют в C#
// через postMessage префиксами SYNC: / SENDRES:, а не через return.
internal static class BrowserBridge
{
internal const string BridgeScript = """
(function(){
if (window.__LERON_BRIDGE__) return;
window.__LERON_BRIDGE__ = true;
var lastText = '';
var lastChange = 0;
window.__LERON_STREAMING__ = false;
window.__LERON_STREAM_TEXT__ = '';
window.__LERON_STREAM_TIME__ = 0;
window.__LERON_STREAM_START__ = 0;
window.__LERON_STREAM_ID__ = '';
window.__LERON_SEEN_STREAM__ = false;
window.__LERON_REQID__ = '';
function assistantEls(){
var candidates = [
'[data-testid="assistant-message"]',
'[data-message-role="assistant"]',
'[class*="message-assistant"]',
'[class*="assistant-message"]',
'[class*="response-message"]',
'.assistant-message',
'[data-role="assistant"]',
'[class*="bot-message"]',
'[class*="ai-message"]',
'[class*="markdown-body"]'
];
for (var i=0;i<candidates.length;i++){
var els = document.querySelectorAll(candidates[i]);
if (els.length > 0) return Array.prototype.slice.call(els);
}
var containers = document.querySelectorAll('[class*="chat-message"], [class*="message-item"]');
var out = [];
for (var j=0;j<containers.length;j++){
var cls = (typeof containers[j].className === 'string' ? containers[j].className : '').toLowerCase();
if (cls.indexOf('user')>=0 || cls.indexOf('human')>=0) continue;
if (containers[j].querySelector('[class*="markdown"], [class*="prose"], [class*="content"]'))
out.push(containers[j]);
}
return out;
}
function extractText(el){
var inner = el.querySelector('[class*="markdown"]') || el.querySelector('[class*="content"]') ||
el.querySelector('[class*="prose"]') || el;
return (inner.innerText || '').trim();
}
function findStopButton(){
var btns = Array.prototype.slice.call(document.querySelectorAll('button'));
for (var i=0;i<btns.length;i++){
var b = btns[i];
var label = (b.getAttribute('aria-label') || '').toLowerCase();
var text = (b.innerText || '').toLowerCase();
if (label.indexOf('stop')>=0 || label.indexOf('cancel')>=0 || label.indexOf('останов')>=0 ||
text.indexOf('stop')>=0 || text.indexOf('остановить')>=0) return b;
}
return null;
}
function post(msg){
if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg);
}
function reportAi(text, id){
if (!text) return;
window.__LERON_LAST_AI__ = text;
lastText = text;
lastChange = Date.now();
post({ action: 'aiResponse', text: text, reqid: id || window.__LERON_REQID__ });
}
function finishStream(){
window.__LERON_STREAMING__ = false;
if (window.__LERON_EXPECT__ && window.__LERON_STREAM_TEXT__) {
window.__LERON_EXPECT__ = false;
reportAi(window.__LERON_STREAM_TEXT__, window.__LERON_STREAM_ID__);
}
}
function check(){
if (!window.__LERON_EXPECT__) return;
if (window.__LERON_STREAMING__) {
var now = Date.now();
var base = window.__LERON_STREAM_TIME__ || window.__LERON_STREAM_START__;
var idle = now - base;
var total = now - window.__LERON_STREAM_START__;
if ((base && idle > 4000) || total > 90000) finishStream();
return;
}
if (window.__LERON_SEEN_STREAM__) return;
if (findStopButton()) return;
var els = assistantEls();
if (els.length === 0) return;
var cur = extractText(els[els.length-1]);
if (cur !== lastText){ lastText = cur; lastChange = Date.now(); }
if ((Date.now() - lastChange) < 3500) return;
if (!cur || cur === window.__LERON_LAST_AI__) return;
window.__LERON_EXPECT__ = false;
reportAi(cur, window.__LERON_REQID__);
}
function startObserve(){
if (!document.body){ setTimeout(startObserve, 100); return; }
var observer = new MutationObserver(check);
observer.observe(document.body, { childList:true, subtree:true, characterData:true });
setInterval(check, 800);
}
startObserve();
var origFetch = window.fetch;
window.fetch = async function(){
var response = await origFetch.apply(this, arguments);
var url = typeof arguments[0] === 'string' ? arguments[0] : (arguments[0] && arguments[0].url || '');
if (response.ok && (url.indexOf('/chat')>=0 || url.indexOf('/completion')>=0)) {
var myId = window.__LERON_REQID__;
window.__LERON_SEEN_STREAM__ = true;
window.__LERON_STREAMING__ = true;
window.__LERON_STREAM_TEXT__ = '';
window.__LERON_STREAM_ID__ = myId;
window.__LERON_STREAM_START__ = Date.now();
window.__LERON_STREAM_TIME__ = 0;
try {
var clone = response.clone();
var reader = clone.body.getReader();
var decoder = new TextDecoder();
var full = '';
var NL = String.fromCharCode(10);
(async function(){
try {
while(true){
var r = await reader.read();
if (r.done){
window.__LERON_STREAMING__ = false;
if (window.__LERON_EXPECT__ && full){
window.__LERON_EXPECT__ = false;
reportAi(full, myId);
}
break;
}
var chunk = decoder.decode(r.value, {stream:true});
var gotData = false;
var lines = chunk.split(NL);
for (var i=0;i<lines.length;i++){
if (lines[i].indexOf('data: ')===0){
var data = lines[i].substring(6).trim();
if (data==='[DONE]') continue;
try {
var json = JSON.parse(data);
var delta = (json.choices && json.choices[0] && json.choices[0].delta && json.choices[0].delta.content) || '';
if (delta){
full += delta;
gotData = true;
window.__LERON_STREAM_TEXT__ = full;
post({ action: 'aiStream', text: full, delta: delta });
}
} catch(e){}
}
}
if (gotData) window.__LERON_STREAM_TIME__ = Date.now();
}
} catch(e){ window.__LERON_STREAMING__ = false; }
})();
} catch(e){ window.__LERON_STREAMING__ = false; }
}
return response;
};
setInterval(function(){
var c = document.querySelector('iframe[src*="challenges.cloudflare.com"], #challenge-stage, #px-captcha, [class*="captcha"], [id*="captcha"]');
var t = (document.body ? document.body.innerText : '').slice(0,3000).toLowerCase();
var hit = !!c || t.indexOf('verify you are human')>=0 || t.indexOf('checking your browser')>=0 || t.indexOf('проверьте, что вы человек')>=0;
if (hit) post({ action: 'captcha' });
}, 3000);
})();
""";

// Синхронизация модели (3.8 max) и режима мышления ПЕРЕД каждой отправкой.
// Триггеры ищем ТОЧНЫМИ селекторами из реального HTML Qwen:
//   модель  — .wms-trigger / .wms-trigger__text (role=button, aria-haspopup=listbox)
//   мышление — .qwen-thinking-selector .qwen-chat-v2-dropdown-menu-select(-label)
// Пункты меню после открытия — по тексту (меню рендерится в портале вне триггера).
// Результат уходит в C# через postMessage('SYNC:...'), т.к. ExecuteScriptAsync
// не ждёт Promise и вернул бы "{}".
internal const string SyncUiScript = @"
(async function() {
var wantThink = __THINK__;
function wait(ms) { return new Promise(function(r) { setTimeout(r, ms); }); }
function post(m) { if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(m); }
function vis(el) {
if (!el) return false;
if (el.getClientRects && el.getClientRects().length > 0) return true;
return el.offsetParent !== null;
}
function txt(el) { return (el && el.innerText ? el.innerText : '').trim(); }
function safeToken(s) { return (s ? s : '?').replace(/[\s|]+/g, '-'); }
function fire(el, type) {
if (!el) return;
var opts = { bubbles: true, cancelable: true, view: window, button: 0, buttons: 1, pointerId: 1, pointerType: 'mouse', isPrimary: true };
var ev = null;
try {
if (window.PointerEvent) ev = new PointerEvent(type, opts);
else ev = new MouseEvent(type, opts);
} catch (e) {
try { ev = new MouseEvent(type, opts); } catch (e2) {
try {
ev = document.createEvent('MouseEvents');
ev.initMouseEvent(type, true, true, window, 0, 0, 0, 0, 0, false, false, false, false, 0, null);
} catch (e3) { ev = null; }
}
}
if (ev) el.dispatchEvent(ev);
}
function fullClick(el) {
if (!el) return;
fire(el, 'pointerover');
fire(el, 'mouseover');
fire(el, 'pointerdown');
fire(el, 'mousedown');
fire(el, 'pointerup');
fire(el, 'mouseup');
fire(el, 'click');
}
// Поиск пункта ОТКРЫТОГО меню по тексту (портал вне excludeRoot).
function findItem(fn, maxLen, excludeRoot) {
var els = document.querySelectorAll('div, span, button, li, p, a');
var best = null; var bLen = 1e9;
for (var i = 0; i < els.length; i++) {
var el = els[i];
if (!vis(el)) continue;
if (excludeRoot && (el === excludeRoot || excludeRoot.contains(el))) continue;
var t = txt(el);
if (!t || t.length > maxLen) continue;
if (fn(t.toLowerCase()) && t.length < bLen) { best = el; bLen = t.length; }
}
return best;
}
// Открыть меню: полная последовательность событий по триггеру и родителям,
// после каждой попытки проверяем, что меню реально открылось. Два прохода.
async function openMenu(trigger, isOpenFn) {
if (!trigger) return false;
var targets = [];
var el = trigger;
for (var i = 0; el && i < 4; i++) { targets.push(el); el = el.parentElement; }
for (var pass = 0; pass < 2; pass++) {
for (var t = 0; t < targets.length; t++) {
fullClick(targets[t]);
await wait(pass === 0 ? 250 : 450);
if (isOpenFn()) return true;
}
}
return isOpenFn();
}
var report = [];
try {
await wait(200);
// ── модель ────────────────────────────────────────────────
var modelTextEl = document.querySelector('.wms-trigger__text');
var modelTrigger = document.querySelector('.wms-trigger');
if (!modelTrigger && modelTextEl) modelTrigger = modelTextEl;
if (modelTrigger) {
var cm = txt(modelTextEl || modelTrigger).toLowerCase();
if (cm.indexOf('3.8') >= 0 && cm.indexOf('max') >= 0) {
report.push('model:ok');
} else {
var modelItemFn = function(t) {
return (t.indexOf('3.8') >= 0 && t.indexOf('max') >= 0) ||
(t.indexOf('qwen') >= 0 && t.indexOf('max') >= 0);
};
var modelOpen = function() { return !!findItem(modelItemFn, 80, modelTrigger); };
var mOpened = await openMenu(modelTrigger, modelOpen);
var mItem = mOpened ? findItem(modelItemFn, 80, modelTrigger) : null;
if (mItem) {
fullClick(mItem);
await wait(1500);
var mNew = txt(document.querySelector('.wms-trigger__text')).toLowerCase();
if (mNew.indexOf('3.8') >= 0 && mNew.indexOf('max') >= 0) report.push('model:ok');
else report.push('model:' + safeToken(mNew));
} else {
report.push('model:menu-fail');
}
}
} else {
report.push('model:no-ui');
}
// ── мышление ──────────────────────────────────────────────
var thinkRoot = document.querySelector('.qwen-thinking-selector');
var thinkBtn = thinkRoot ? thinkRoot.querySelector('.qwen-chat-v2-dropdown-menu-select') : null;
var thinkLabel = thinkRoot ? thinkRoot.querySelector('.qwen-chat-v2-dropdown-menu-select-label') : null;
if (thinkBtn || thinkLabel) {
var ct = txt(thinkLabel || thinkBtn).toLowerCase();
var isThink = ct.indexOf('мышл') >= 0 || ct.indexOf('think') >= 0 || ct.indexOf('reason') >= 0;
if (isThink === wantThink) {
report.push('think:ok');
} else {
var words = wantThink ? ['мышление', 'мышл', 'thinking', 'think'] : ['быстрый', 'быстр', 'fast'];
var thinkItemFn = function(t) {
for (var w = 0; w < words.length; w++) { if (t.indexOf(words[w]) >= 0) return true; }
return false;
};
var thinkOpen = function() { return !!findItem(thinkItemFn, 40, thinkRoot); };
var tOpened = await openMenu(thinkBtn || thinkLabel, thinkOpen);
var tItem = tOpened ? findItem(thinkItemFn, 40, thinkRoot) : null;
if (tItem) {
fullClick(tItem);
await wait(400);
var tNew = txt(thinkRoot ? thinkRoot.querySelector('.qwen-chat-v2-dropdown-menu-select-label') : null).toLowerCase();
var nowThink = tNew.indexOf('мышл') >= 0 || tNew.indexOf('think') >= 0;
if (nowThink === wantThink) report.push('think:ok');
else report.push('think:' + safeToken(tNew));
} else {
report.push('think:menu-fail');
}
}
} else {
report.push('think:no-ui');
}
} catch (e) {
report.push('sync-err:' + safeToken(e && e.message ? e.message : 'unknown'));
}
post('SYNC:' + report.join(' '));
return 'sync-started';
})();";

// Отправка: сначала ждём окончания старого стрима, ПОТОМ взводим EXPECT и новый REQID.
// send() вызывается ровно ОДИН раз. Статус уходит в C# через postMessage('SENDRES:...').
internal const string SendScript = @"
(async function() {
var text = __TEXT__;
var reqid = '__REQID__';
function wait(ms) { return new Promise(function(r) { setTimeout(r, ms); }); }
function post(m) { if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(m); }
function done(code) { post('SENDRES:' + code); }
function inputEl() { return document.querySelector('textarea') || document.querySelector('[contenteditable=""true""]'); }
function isEmpty() {
var el = inputEl();
if (!el) return true;
if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') return el.value === '';
return (el.innerText || '') === '';
}
function sendBtn() {
var btns = Array.prototype.slice.call(document.querySelectorAll('button'));
for (var j = 0; j < btns.length; j++) {
var b = btns[j];
if (!b.disabled && (b.getAttribute('aria-label') || '').toLowerCase().indexOf('send') >= 0) return b;
}
return null;
}
function send() {
var sb = sendBtn();
if (sb) { sb.click(); return; }
var el = inputEl();
if (el) el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
}
try {
var waited = 0;
while (window.__LERON_STREAMING__ && waited < 15000) { await wait(250); waited += 250; }
window.__LERON_REQID__ = reqid;
window.__LERON_EXPECT__ = true;
window.__LERON_SEEN_STREAM__ = false;
window.__LERON_STREAM_TEXT__ = '';
var input = inputEl();
if (!input) { done('NO_INPUT'); return 'started'; }
input.focus();
var isTa = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
function put(chunk) {
if (isTa) {
var s = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
s.call(input, input.value + chunk);
input.dispatchEvent(new Event('input', { bubbles: true }));
} else {
document.execCommand('insertText', false, chunk);
}
}
function clearInput() {
if (isTa) {
var s2 = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
s2.call(input, '');
input.dispatchEvent(new Event('input', { bubbles: true }));
} else {
document.execCommand('selectAll', false, null);
document.execCommand('delete', false, null);
}
}
clearInput();
if (text.length > 200) {
put(text);
await wait(200);
} else {
var i = 0;
await new Promise(function(resolve) {
(function step() {
if (i >= text.length) { setTimeout(resolve, 150); return; }
var take = 2 + Math.floor(Math.random() * 4);
put(text.substring(i, i + take));
i += take;
setTimeout(step, 10 + Math.random() * 25);
})();
});
}
send();
var waitedAfterSend = 0;
while (waitedAfterSend < 3000) {
await wait(250);
waitedAfterSend += 250;
if (isEmpty()) { done('OK'); return 'started'; }
if (window.__LERON_STREAMING__ || window.__LERON_SEEN_STREAM__) { done('OK'); return 'started'; }
}
post({ action: 'sendfail' });
done('NOT_SENT');
return 'started';
} catch (e) {
done('ERR:' + (e && e.message ? e.message : 'unknown'));
return 'started';
}
})();";
}