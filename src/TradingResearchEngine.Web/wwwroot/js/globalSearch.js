window.globalSearch = {
    register: function (dotNetRef) {
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OpenFromJs');
            }
        });
    }
};
