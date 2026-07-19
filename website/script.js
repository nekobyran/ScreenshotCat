const year = document.querySelector('[data-year]');
const checksum = document.querySelector('[data-checksum]');
const copyButton = document.querySelector('[data-copy-checksum]');
const toast = document.querySelector('[data-toast]');

if (year) year.textContent = new Date().getFullYear();

function showToast(message) {
  if (!toast) return;
  toast.textContent = message;
  toast.classList.add('is-visible');
  window.setTimeout(() => toast.classList.remove('is-visible'), 1800);
}

copyButton?.addEventListener('click', async () => {
  const value = checksum?.textContent?.trim();
  if (!value) return;
  try {
    await navigator.clipboard.writeText(value);
    showToast('SHA-256 已复制');
  } catch {
    showToast('请选择校验值后手动复制');
  }
});

document.querySelectorAll('[data-download]').forEach((link) => {
  link.addEventListener('click', () => showToast('正在前往 GitHub 下载…'));
});
