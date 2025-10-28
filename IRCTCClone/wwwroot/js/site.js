// --- Small helper for AJAX station autocomplete ---

// --- Autocomplete Setup Function ---
function setupAutocomplete(inputId, listId) {
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
    });

    // Hide list when clicking elsewhere
    $(document).on("click", function (e) {
        if (!$(e.target).closest($list).length && !$(e.target).is($input)) {
            $list.hide();
        }
    });
}*/

// --- DOM Ready ---
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
    });

    // --- Swap Station Logic ---
    var swapBtn = document.getElementById("swapStations");
    var fromSelect = document.getElementById("fromStation");
    var toSelect = document.getElementById("toStation");

    if (swapBtn && fromSelect && toSelect) {
        swapBtn.onclick = function () {
            var tempValue = fromSelect.value;
            fromSelect.value = toSelect.value;
            toSelect.value = tempValue;
        };
    }
});





//function searchStations(query, callback) {
//    $.ajax({
//        url: '/Train/SearchStations', // corrected to match your controller method
//        data: { term: query },
//        success: function (data) {
//            callback(data);
//        }
//    });
//}
function togglePasswordVisibility() {
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
}
