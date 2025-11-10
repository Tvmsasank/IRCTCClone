$(function () {
    ///////////////////////////////////////////////////////////////////////////
    // 2. CANCEL BOOKING CONFIRMATION
    ///////////////////////////////////////////////////////////////////////////
    $(document).on('click', '.cancel-btn', function (e) {
        if (!confirm('Are you sure you want to cancel this booking?')) {
            e.preventDefault();
        }
    });


    ///////////////////////////////////////////////////////////////////////////
    // 3. AUTOCOMPLETE
    ///////////////////////////////////////////////////////////////////////////
    function debounce(fn, delay) {
        let timer;
        return function () {
            const context = this;
            const args = arguments;
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(context, args), delay);
        };
    }

    function wireAutocomplete($input, $list) {
        function fetchStations(term, callback) {
            $.get('/Train/GetStations', { term: term }, function (data) {
                callback(data || []);
            }); 
        }

        function renderList(items) {
            $list.empty();
            if (!items.length) {
                $list.hide();
                return;
            }

            items.forEach(function (s, i) {
                const $item = $('<div class="autocomplete-item">')
                    .text(s.name + ' (' + s.code + ')')
                    .data('id', s.id);

                if (i === 0) $item.addClass('active'); // ✅ highlight first item
                $list.append($item);
            });

            $list.show();
        }

        // Typing triggers search
        $input.on('input', debounce(function () {
            const term = $input.val().trim();
            if (!term) {
                $list.hide();
                if ($input.is('#fromStation')) $('#fromStationId').val('');
                if ($input.is('#toStation')) $('#toStationId').val('');
                return;
            }
            fetchStations(term, renderList);
        }, 200));

        // Selecting from dropdown (click)
        $list.on('click', '.autocomplete-item', function () {
            selectItem($(this));
        });

        // Keyboard navigation
        $input.on('keydown', function (e) {
            const $items = $list.find('.autocomplete-item');
            let $active = $items.filter('.active');
            let index = $active.index();

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                $items.removeClass('active');
                if (index < $items.length - 1) index++;
                $items.eq(index).addClass('active');
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                $items.removeClass('active');
                if (index > 0) index--;
                $items.eq(index).addClass('active');
            } else if (e.key === 'Enter') {
                e.preventDefault();
                if ($active.length) {
                    selectItem($active);
                } else if ($items.length) {
                    selectItem($items.first());
                }
            }
        });

        function selectItem($item) {
            const text = $item.text();
            const id = $item.data('id');
            $input.val(text);
            $list.hide();

            if ($input.is('#fromStation')) $('#fromStationId').val(id);
            if ($input.is('#toStation')) $('#toStationId').val(id);
        }

        // Hide dropdown when clicking outside
        $(document).on('click', function (e) {
            if (!$(e.target).closest($input).length && !$(e.target).closest($list).length) {
                $list.hide();
            }
        });

    }

    // Initialize both inputs
    wireAutocomplete($('#fromStation'), $('#fromStationList'));
    wireAutocomplete($('#toStation'), $('#toStationList'));


    ///////////////////////////////////////////////////////////////////////////
    // 4. SWAP STATION LOGIC
    ///////////////////////////////////////////////////////////////////////////
    $('#swapStations').on('click', function () {
        const fromText = $('#fromStation').val();
        const toText = $('#toStation').val();
        $('#fromStation').val(toText);
        $('#toStation').val(fromText);

        const fromId = $('#fromStationId').val();
        const toId = $('#toStationId').val();
        $('#fromStationId').val(toId);
        $('#toStationId').val(fromId);
    });
});
