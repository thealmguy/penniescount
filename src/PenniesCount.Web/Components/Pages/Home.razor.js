let chart;
const colors = ["#1b6ec2", "#2d7f5e", "#8a5bb5", "#a24a3b", "#ae7400",
    "#147d92", "#ba4b83", "#566322", "#595959", "#969696"];

export function render(canvas, labels, values, exact, total) {
    if (!window.Chart) throw new Error("Chart.js is unavailable.");
    dispose();
    chart = new window.Chart(canvas, {
        type: "doughnut",
        data: {
            labels,
            datasets: [{ data: values, backgroundColor: colors }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { position: "bottom", onClick: () => {} },
                tooltip: {
                    callbacks: {
                        label: context => `${context.label}: ${exact[context.dataIndex]}`,
                        footer: () => `Total allocated: ${total}`
                    }
                }
            }
        }
    });
}

export function dispose() {
    chart?.destroy();
    chart = undefined;
}
