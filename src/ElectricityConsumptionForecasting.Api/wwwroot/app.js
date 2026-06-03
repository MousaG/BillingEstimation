const api = {
  forecast: "/api/forecast/monthly/customer",
  batch: "/api/forecast/monthly/run-batch",
  dashboard: "/api/forecast/monthly/dashboard",
  seed: "/api/admin/sample-data/seed",
  rebuildFeatures: "/api/admin/features/rebuild",
  configs: "/api/admin/configs"
};

const viewMeta = {
  forecast: ["پیش بینی مشترک", "شناسه قبض و ماه هدف را وارد کنید."],
  sample: ["داده نمونه", "سناریوی تستی target و مشترک های مشابه را بسازید."],
  batch: ["اجرای گروهی", "پیش بینی را برای یک محدوده شرکت اجرا کنید."],
  dashboard: ["داشبورد", "خلاصه خروجی های آخرین اجرا را ببینید."],
  config: ["تنظیمات", "Featureها و thresholdهای موتور را مدیریت کنید."]
};

document.querySelectorAll(".nav-item").forEach(button => {
  button.addEventListener("click", () => showView(button.dataset.view));
});

document.getElementById("quickSeedButton").addEventListener("click", async () => {
  const result = await postJson(api.seed, formToObject(document.getElementById("sampleForm")));
  writeJson("sampleJson", result);
  toast("داده نمونه ساخته شد.");
  showView("forecast");
});

bindForm("forecastForm", async form => {
  const payload = formToObject(form);
  payload.useSmartMeterIfAvailable = Boolean(payload.useSmartMeterIfAvailable);
  const result = await postJson(api.forecast, payload);
  renderForecast(result);
  writeJson("forecastJson", result);
});

bindForm("sampleForm", async form => {
  const result = await postJson(api.seed, formToObject(form));
  writeJson("sampleJson", result);
  toast(`${result.profilesCreated} پروفایل نمونه ساخته شد.`);
});

bindForm("batchForm", async form => {
  const payload = formToObject(form);
  if (!payload.tariffType) payload.tariffType = null;
  const result = await postJson(api.batch, payload);
  writeJson("batchJson", result);
});

bindForm("dashboardForm", async form => {
  const payload = formToObject(form);
  const query = new URLSearchParams(payload);
  const result = await getJson(`${api.dashboard}?${query}`);
  renderDashboard(result);
  writeJson("dashboardJson", result);
});

bindForm("featureForm", async form => {
  const result = await postJson(api.rebuildFeatures, formToObject(form));
  writeJson("configJson", result);
  toast(`${result.featuresBuilt} feature ساخته شد.`);
});

bindForm("configForm", async form => {
  const payload = formToObject(form);
  payload.isActive = Boolean(payload.isActive);
  const result = await postJson(api.configs, payload);
  writeJson("configJson", result);
  await loadConfigs();
});

loadConfigs().catch(() => {});

function showView(view) {
  document.querySelectorAll(".nav-item").forEach(x => x.classList.toggle("active", x.dataset.view === view));
  document.querySelectorAll(".view").forEach(x => x.classList.toggle("active", x.id === view));
  document.getElementById("viewTitle").textContent = viewMeta[view][0];
  document.getElementById("viewSubtitle").textContent = viewMeta[view][1];
  if (view === "config") loadConfigs().catch(error => toast(error.message));
}

function bindForm(id, handler) {
  const form = document.getElementById(id);
  form.addEventListener("submit", async event => {
    event.preventDefault();
    const button = form.querySelector("button[type='submit']");
    button.disabled = true;
    setStatus("در حال اجرا");
    try {
      await handler(form);
      setStatus("آماده");
    } catch (error) {
      toast(error.message);
      setStatus("خطا");
    } finally {
      button.disabled = false;
    }
  });
}

function formToObject(form) {
  const data = new FormData(form);
  const result = {};
  for (const element of form.elements) {
    if (!element.name) continue;
    if (element.type === "checkbox") {
      result[element.name] = element.checked;
      continue;
    }
    const value = data.get(element.name);
    if (value === null || value === "") {
      result[element.name] = null;
    } else if (element.type === "number") {
      result[element.name] = Number(value);
    } else {
      result[element.name] = value;
    }
  }
  return result;
}

async function getJson(url) {
  const response = await fetch(url);
  return parseResponse(response);
}

async function postJson(url, payload) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  return parseResponse(response);
}

async function parseResponse(response) {
  const text = await response.text();
  let body = null;
  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  }
  if (!response.ok) {
    const message = body?.errors?.join?.(", ") || body?.error || body || `HTTP ${response.status}`;
    throw new Error(message);
  }
  return body;
}

function renderForecast(result) {
  document.getElementById("predictedValue").textContent = result.predictedConsumption ?? "-";
  document.getElementById("confidenceValue").textContent = `${Math.round((result.confidence ?? 0) * 100)}% ${result.confidenceLevel}`;
  document.getElementById("similarValue").textContent = result.similarSubscribersCount ?? 0;
  document.getElementById("riskValue").textContent = result.riskLevel ?? "-";
}

function renderDashboard(result) {
  document.getElementById("totalResults").textContent = result.totalResults;
  document.getElementById("forecastableCount").textContent = result.forecastableCount;
  document.getElementById("expertReviewCount").textContent = result.expertReviewCount;
  document.getElementById("averageConfidence").textContent = `${Math.round((result.averageConfidence ?? 0) * 100)}%`;
}

async function loadConfigs() {
  const rows = document.getElementById("configRows");
  const configs = await getJson(api.configs);
  rows.innerHTML = "";
  for (const item of configs) {
    const tr = document.createElement("tr");
    tr.innerHTML = `<td>${escapeHtml(item.name)}</td><td>${escapeHtml(item.value)}</td><td>${item.isActive ? "فعال" : "غیرفعال"}</td><td>${escapeHtml(item.description ?? "")}</td>`;
    rows.appendChild(tr);
  }
}

function writeJson(id, value) {
  document.getElementById(id).textContent = JSON.stringify(value, null, 2);
}

function toast(message) {
  const element = document.getElementById("toast");
  element.textContent = message;
  element.hidden = false;
  clearTimeout(window.toastTimer);
  window.toastTimer = setTimeout(() => element.hidden = true, 3600);
}

function setStatus(text) {
  document.getElementById("connectionText").textContent = text;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
