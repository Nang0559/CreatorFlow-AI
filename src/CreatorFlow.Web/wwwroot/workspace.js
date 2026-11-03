window.creatorFlowWorkspace = {
    async postWithCsrf(url) {
        const tokenResponse = await fetch('/auth/csrf', { credentials: 'same-origin' });
        if (!tokenResponse.ok) throw new Error('Không lấy được mã bảo vệ phiên. Hãy tải lại trang.');
        const token = (await tokenResponse.json()).requestToken;
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'X-CSRF-TOKEN': token }
        });
        if (!response.ok) throw new Error('Máy chủ từ chối thao tác (HTTP ' + response.status + ').');
        return response;
    },
    async select(tenantId) {
        await this.postWithCsrf('/auth/select-workspace/' + encodeURIComponent(tenantId));
    },
    async logout() {
        const tokenResponse = await fetch('/auth/csrf', { credentials: 'same-origin' });
        if (!tokenResponse.ok) throw new Error('Không lấy được mã bảo vệ phiên.');
        const token = (await tokenResponse.json()).requestToken;
        const form = document.createElement('form');
        form.method = 'POST';
        form.action = '/auth/logout';
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = '__RequestVerificationToken';
        input.value = token;
        form.appendChild(input);
        document.body.appendChild(form);
        form.submit();
    }
};
