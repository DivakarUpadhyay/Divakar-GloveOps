// Divakar GloveOps — shared jQuery helpers
window.gloveops = (function ($) {
    'use strict';

    function token() {
        return $('#af-token input[name="__RequestVerificationToken"]').val();
    }

    // POST JSON with the anti-forgery token header; resolves with the JSON body.
    function postJson(url, data) {
        return $.ajax({
            url: url,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(data || {}),
            headers: { 'RequestVerificationToken': token() }
        });
    }

    function errorText(xhr) {
        return (xhr.responseJSON && (xhr.responseJSON.error || xhr.responseJSON.title)) ||
            'Something went wrong (HTTP ' + xhr.status + ').';
    }

    // Client-side quick filter for any table: <input data-filter-table="#tbl">
    $(document).on('input', '[data-filter-table]', function () {
        var term = $(this).val().toLowerCase();
        $($(this).data('filter-table')).find('tbody tr').each(function () {
            $(this).toggle($(this).text().toLowerCase().indexOf(term) > -1);
        });
    });

    return { postJson: postJson, errorText: errorText };
})(jQuery);
