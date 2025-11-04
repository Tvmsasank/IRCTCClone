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






































































































/*// --- site.js ---
$(document).ready(function () {

    function updateTotal() {
        var fare = parseFloat($("#totalFare").data("fare") || 0);
        var count = $("#passengers .passenger-card").length;
        $("#totalFare").text((count * fare).toFixed(2));
    }

    // --- Passenger Handling ---
    $("#addPassenger").on("click", function () {
        var passengerHtml = `
            <div class="passenger-card">
                <input name="passengerNames" placeholder="Name" required />
                <input name="passengerAges" type="number" placeholder="Age" required />
                <select name="passengerGenders" required>
                    <option value="">Select Gender</option>
                    <option value="Male">Male</option>
                    <option value="Female">Female</option>
                    <option value="Other">Other</option>
                </select>
                <select name="passengerBerths" required>
                    <option value="">Select Berth</option>
                    <option value="Lower">Lower</option>
                    <option value="Middle">Middle</option>
                    <option value="Upper">Upper</option>
                    <option value="Side Lower">Side Lower</option>
                    <option value="Side Upper">Side Upper</option>
                </select>
                <button type="button" class="remove-passenger">❌</button>
            </div>
        `;
        $("#passengers").append(passengerHtml);
        updateTotal();
    });

    $(document).on("click", ".remove-passenger", function () {
        $(this).closest(".passenger-card").remove();
        updateTotal();
    });

    updateTotal(); // initial total

    // --- Cancel Booking Confirmation ---
    $(document).on('click', '.cancel-btn', function (e) {
        if (!confirm('Are you sure you want to cancel this booking?')) {
            e.preventDefault();
        }
    });

    // --- Swap Station Logic ---
    $(function () {
        wireAutocomplete($('#fromStation'), $('#fromStationList'));
        wireAutocomplete($('#toStation'), $('#toStationList'));

        // 🔁 Swap
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
});
*/


































































































// --- Small helper for AJAX station autocomplete ---

// --- Autocomplete Setup Function ---
/*function setupAutocomplete(inputId, listId) {
    const $input = $("#" + inputId);
    const $list = $("#" + listId);

    $input.on("input", function () {
        const term = $input.val().trim();
        if (term.length < 1) {
            $list.empty().hide();
            return;
        }

        $.getJSON("/Train/SearchStations", { term: term }, function (data) {
            $list.empty();
            if (!data || data.length === 0) {
                $list.hide();
                return;
            }

            data.forEach(st => {
                const item = $("<li>")
                    .text(st.name + " (" + st.code + ")")
                    .addClass("suggestion-item")
                    .on("click", function () {
                        $input.val(st.name + " (" + st.code + ")");
                        $list.empty().hide();
                    });
                $list.append(item);
            });

            $list.show();
        });
    });*/

    // Hide list when clicking elsewhere
/*    $(document).on("click", function (e) {
        if (!$(e.target).closest($list).length && !$(e.target).is($input)) {
            $list.hide();
        }
    });
}*/

/*// --- DOM Ready ---
$(document).ready(function () {
    // Initialize autocomplete for both inputs
    setupAutocomplete("fromStation", "fromSuggestions");
    setupAutocomplete("toStation", "toSuggestions");

    // --- Cache fare ---
    var fare = parseFloat($("#totalFare").data("fare") || 0);

    // Function to update total fare
    function updateTotal() {
        var count = $("#passengers .passenger-card").length;
        $("#totalFare").text((count * fare).toFixed(2));
    }

    // --- Passenger Handling ---
    $("#addPassenger").on("click", function () {
        var passengerHtml = `
            <div class="passenger-card">
                <input name="passengerNames" placeholder="Name" required />
                <input name="passengerAges" type="number" placeholder="Age" required />
                <select name="passengerGenders" required>
                    <option value="">Select Gender</option>
                    <option value="Male">Male</option>
                    <option value="Female">Female</option>
                    <option value="Other">Other</option>
                </select>
                <select name="passengerBerths" required>
                    <option value="">Select Berth</option>
                    <option value="Lower">Lower</option>
                    <option value="Middle">Middle</option>
                    <option value="Upper">Upper</option>
                    <option value="Side Lower">Side Lower</option>
                    <option value="Side Upper">Side Upper</option>
                </select>
                <button type="button" class="remove-passenger">❌</button>
            </div>
        `;
        $("#passengers").append(passengerHtml);
        updateTotal();
    });

    // Remove passenger
    $(document).on("click", ".remove-passenger", function () {
        $(this).closest(".passenger-card").remove();
        updateTotal();
    });

    // Update total on input change
    $(document).on("input change", "#passengers input, #passengers select", updateTotal);
    updateTotal();

    // --- Cancel Booking Confirmation ---
    $(".cancel-btn").on("click", function (e) {
        if (!confirm("Are you sure you want to cancel this booking?")) {
            e.preventDefault();
        }
    });*/

    // --- Swap Station Logic ---
/*  var swapBtn = document.getElementById("swapStations");
    var fromSelect = document.getElementById("fromStation");
    var toSelect = document.getElementById("toStation");

    if (swapBtn && fromSelect && toSelect) {
        swapBtn.onclick = function () {
            var tempValue = fromSelect.value;
            fromSelect.value = toSelect.value;
            toSelect.value = tempValue;
        };
    }
});*/

//function searchStations(query, callback) {
//    $.ajax({
//        url: '/Train/SearchStations', // corrected to match your controller method
//        data: { term: query },
//        success: function (data) {
//            callback(data);
//        }
//    });
//}

/*function togglePasswordVisibility() {
    const passwordField = document.getElementById('passwordmain');
    const icon = document.querySelector('#togglePassword i');

    // Toggle the type of the input between 'password' and 'text'
    if (passwordField.type === 'password') {
        passwordField.type = 'text';
        icon.classList.remove('fa-eye');
        icon.classList.add('fa-eye-slash');
    } else {
        passwordField.type = 'password';
        icon.classList.remove('fa-eye-slash');
        icon.classList.add('fa-eye');
    }
}*/

