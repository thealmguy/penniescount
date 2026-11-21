const instances = new WeakMap();
const colors = ["#1b6ec2", "#2d7f5e", "#8a5bb5", "#a24a3b"];
const gbp = new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" });

export function render(canvas, labels, values, exact, totals) {
    if (!window.Chart) throw new Error("Chart.js is unavailable.");
    instances.get(canvas)?.destroy();
    const datasets = ["Short term", "Medium term", "Long term", "Historic"].map((label, index) => ({
        label,
        data: values.map(month => month[index]),
        exact: exact.map(month => month[index]),
        backgroundColor: colors[index],
        stack: "funding"
    }));
    instances.set(canvas, new window.Chart(canvas, {
        type: "bar",
        data: { labels, datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: "index", intersect: false },
            scales: {
                x: { stacked: true, title: { display: true, text: "Month" } },
                y: {
                    stacked: true,
                    beginAtZero: true,
                    title: { display: true, text: "Allocation needed (GBP)" },
                    ticks: { callback: value => gbp.format(value) }
                }
            },
            plugins: {
                legend: { onClick: () => {} },
                tooltip: {
                    callbacks: {
                        label: context => `${context.dataset.label}: ${context.dataset.exact[context.dataIndex]}`,
                        footer: items => `Total: ${totals[items[0].dataIndex]}`
                    }
                }
            }
        }
    }));
}

export function dispose(canvas) {
    instances.get(canvas)?.destroy();
    instances.delete(canvas);
}
